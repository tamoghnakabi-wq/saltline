#!/usr/bin/env python3
"""Package the release builds for distribution.

    python3 Tools/package_release.py [--repo owner/name] [--site https://example.com] [--min-protocol N]
                                     [--min-version X.Y.Z] [--allow-dirty] [--allow-no-update-url]

Reads the version and network protocol from Assets/_Saltline/Scripts/Runtime/Core/BuildInfo.cs and checks that
both release builds (Builds/macOS/SALTLINE.app and Builds/Windows/SALTLINE/) were built from that version, as
release (not development) builds, from the current commit. Then it writes Release/v<version>/:

    SALTLINE-<version>-macOS-arm64.zip     the .app, zipped with ditto so its code signature survives
    SALTLINE-<version>-Windows-x64.zip     the game folder, without Unity's *_DoNotShip debug folders
    SHA256SUMS.txt                          checksums, in the format `shasum -a 256 -c` reads
    release.json                            version, protocol, files, sizes, checksums and download URLs
    version.json                            what the game's update check reads (latest version and the
                                            oldest protocol and version still allowed online)

Each zip is checked after it is written: the Mac one is extracted and its code signature verified, the Windows
one is read back entry by entry. release.json and version.json are also copied to the website (Web/), which
shows the downloads and serves /version.json.

Nothing is uploaded. Publishing (GitHub release, website) is a separate, deliberate step.
"""
import argparse
import datetime
import hashlib
import json
import os
import plistlib
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BUILD_INFO = os.path.join(ROOT, "Assets/_Saltline/Scripts/Runtime/Core/BuildInfo.cs")
MAC_DIR = os.path.join(ROOT, "Builds/macOS")
MAC_APP = os.path.join(MAC_DIR, "SALTLINE.app")
WIN_DIR = os.path.join(ROOT, "Builds/Windows")
WIN_GAME = os.path.join(WIN_DIR, "SALTLINE")
NOTES_DIR = os.path.join(ROOT, "docs/releases")
WEB_DATA = os.path.join(ROOT, "Web/src/data/release.json")
WEB_VERSION = os.path.join(ROOT, "Web/public/version.json")


def fail(msg):
    print(f"package_release: {msg}", file=sys.stderr)
    sys.exit(1)


def read_build_info():
    src = open(BUILD_INFO, encoding="utf-8").read()
    def const(name, pattern):
        m = re.search(rf"\b{name}\s*=\s*{pattern}\s*;", src)
        if not m:
            fail(f"could not read {name} from BuildInfo.cs")
        return m.group(1)
    return {
        "game": const("GameName", r'"([^"]+)"'),
        "version": const("Version", r'"([^"]+)"'),
        "protocol": int(const("NetProtocol", r"(\d+)")),
        "updateUrl": const("UpdateManifestUrl", r'"([^"]*)"'),
    }


def semver(v):
    m = re.fullmatch(r"(\d+)\.(\d+)\.(\d+)", v or "")
    if not m:
        fail(f"'{v}' is not a version of the form X.Y.Z")
    return tuple(int(x) for x in m.groups())


def git(*args):
    return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True).stdout.strip()


def build_stamp(folder, info, label, head, allow_dirty):
    """The build writes SALTLINE.buildinfo.json beside its output. Check it matches what we are releasing."""
    path = os.path.join(folder, "SALTLINE.buildinfo.json")
    if not os.path.isfile(path):
        fail(f"{label}: {os.path.relpath(path, ROOT)} is missing; rebuild with SaltlineBuild")
    stamp = json.load(open(path, encoding="utf-8"))
    if stamp.get("version") != info["version"] or stamp.get("protocol") != info["protocol"]:
        fail(f"{label} build is v{stamp.get('version')} protocol {stamp.get('protocol')}, "
             f"but BuildInfo.cs says v{info['version']} protocol {info['protocol']}; rebuild it")
    if stamp.get("development"):
        fail(f"{label} build is a development build; build without -devbuild")
    if stamp.get("commit") != head:
        fail(f"{label} build is from commit {stamp.get('commit', '?')[:10]}, not the current {head[:10]}; rebuild it")
    if stamp.get("dirty") and not allow_dirty:
        fail(f"{label} build was made with uncommitted changes; commit, rebuild, or pass --allow-dirty")
    return stamp


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def count_files(folder):
    return sum(len(files) + sum(1 for d in dirs if os.path.islink(os.path.join(r, d))) for r, dirs, files in os.walk(folder))


def zip_mac(out_zip):
    # ditto keeps symlinks, extended attributes and the code signature; Finder's Compress does the same.
    subprocess.run(["ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", MAC_APP, out_zip], check=True)
    with tempfile.TemporaryDirectory() as tmp:
        subprocess.run(["ditto", "-x", "-k", out_zip, tmp], check=True)
        app = os.path.join(tmp, "SALTLINE.app")
        r = subprocess.run(["codesign", "--verify", "--deep", "--strict", app], capture_output=True, text=True)
        if r.returncode != 0:
            fail(f"code signature of the zipped app does not verify: {r.stderr.strip()}")
        if count_files(app) != count_files(MAC_APP):
            fail("the zipped app has a different number of files than the build")
        plist = plistlib.load(open(os.path.join(app, "Contents/Info.plist"), "rb"))
    return plist


def zip_windows(out_zip, top):
    names = []
    with zipfile.ZipFile(out_zip, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for root, dirs, files in os.walk(WIN_GAME):
            dirs[:] = sorted(d for d in dirs if "DoNotShip" not in d)
            for f in sorted(files):
                full = os.path.join(root, f)
                arc = os.path.join(top, os.path.relpath(full, WIN_GAME)).replace(os.sep, "/")
                z.write(full, arc)
                names.append(arc)
    with zipfile.ZipFile(out_zip) as z:
        bad = z.testzip()
        if bad:
            fail(f"Windows zip entry {bad} is corrupt")
        if sorted(z.namelist()) != sorted(names):
            fail("Windows zip does not list the files that were written")
        if f"{top}/SALTLINE.exe" not in z.namelist():
            fail("Windows zip has no SALTLINE.exe")
    return len(names)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--repo", help="GitHub owner/name the release will be uploaded to (default: the origin remote)")
    ap.add_argument("--site", help="website base URL, e.g. https://saltline.example (for the download link)")
    ap.add_argument("--min-protocol", type=int, help="oldest network protocol still allowed online (default: this release's)")
    ap.add_argument("--min-version", help="oldest game version still allowed online (default: none)")
    ap.add_argument("--allow-dirty", action="store_true", help="accept builds made with uncommitted changes (test packages only)")
    ap.add_argument("--allow-no-update-url", action="store_true", help="accept builds whose update check has no URL (test packages only)")
    ap.add_argument("--no-web", action="store_true", help="do not copy release.json and version.json into Web/")
    a = ap.parse_args()

    info = read_build_info()
    semver(info["version"])
    if a.min_version:
        semver(a.min_version)
        if semver(a.min_version) > semver(info["version"]):
            fail("--min-version is newer than this release")
    min_protocol = a.min_protocol if a.min_protocol is not None else info["protocol"]
    if min_protocol > info["protocol"]:
        fail("--min-protocol is newer than this release's protocol")
    if not info["updateUrl"] and not a.allow_no_update_url:
        fail("BuildInfo.UpdateManifestUrl is empty, so these builds could never see an update; set it and rebuild, "
             "or pass --allow-no-update-url for a test package")

    head = git("rev-parse", "HEAD")
    if not head:
        fail("not a git checkout")
    if not os.path.isdir(MAC_APP) or not os.path.isfile(os.path.join(WIN_GAME, "SALTLINE.exe")):
        fail("both release builds are needed: Builds/macOS/SALTLINE.app and Builds/Windows/SALTLINE/SALTLINE.exe")
    mac_stamp = build_stamp(MAC_DIR, info, "macOS", head, a.allow_dirty)
    win_stamp = build_stamp(WIN_DIR, info, "Windows", head, a.allow_dirty)

    repo = a.repo
    if not repo:
        m = re.search(r"github\.com[:/]([^/]+/[^/]+?)(?:\.git)?$", git("remote", "get-url", "origin"))
        repo = m.group(1) if m else None
    tag = f"v{info['version']}"
    base_url = f"https://github.com/{repo}/releases/download/{tag}" if repo else None

    out = os.path.join(ROOT, "Release", tag)
    if os.path.isdir(out):
        shutil.rmtree(out)
    os.makedirs(out)

    stem = f"{info['game']}-{info['version']}"
    mac_zip = os.path.join(out, f"{stem}-macOS-arm64.zip")
    win_zip = os.path.join(out, f"{stem}-Windows-x64.zip")

    print(f"Packaging {info['game']} {info['version']} (protocol {info['protocol']}) from {head[:10]}")
    plist = zip_mac(mac_zip)
    if plist.get("CFBundleShortVersionString") != info["version"]:
        fail(f"the app's Info.plist says version {plist.get('CFBundleShortVersionString')}, not {info['version']}")
    win_count = zip_windows(win_zip, f"{stem}-Windows-x64")

    files = []
    for path, platform, label, stamp in [(mac_zip, "macos-arm64", "macOS (Apple silicon)", mac_stamp),
                                         (win_zip, "windows-x64", "Windows x64", win_stamp)]:
        name = os.path.basename(path)
        files.append({
            "platform": platform,
            "label": label,
            "name": name,
            "size": os.path.getsize(path),
            "sha256": sha256(path),
            "url": f"{base_url}/{name}" if base_url else None,
            "built": stamp.get("built"),
        })

    with open(os.path.join(out, "SHA256SUMS.txt"), "w", encoding="utf-8", newline="\n") as f:
        for e in files:
            f.write(f"{e['sha256']}  {e['name']}\n")
    # Check the sums file the way a player would.
    r = subprocess.run(["shasum", "-a", "256", "-c", "SHA256SUMS.txt"], cwd=out, capture_output=True, text=True)
    if r.returncode != 0:
        fail(f"SHA256SUMS.txt does not verify: {r.stdout}{r.stderr}")

    notes_path = os.path.join(NOTES_DIR, f"{info['version']}.md")
    notes = open(notes_path, encoding="utf-8").read().strip() if os.path.isfile(notes_path) else ""
    released = datetime.date.today().isoformat()
    release = {
        "game": info["game"],
        "version": info["version"],
        "tag": tag,
        "protocol": info["protocol"],
        "minimumProtocol": min_protocol,
        "minimumVersion": a.min_version,
        "released": released,
        "commit": head,
        "repo": repo,
        "releasePage": f"https://github.com/{repo}/releases/tag/{tag}" if repo else None,
        "checksums": f"{base_url}/SHA256SUMS.txt" if base_url else None,
        "notes": notes,
        "files": files,
    }
    version = {
        "game": info["game"],
        "latest": info["version"],
        "protocol": info["protocol"],
        "minimumProtocol": min_protocol,
        "minimumVersion": a.min_version or "",
        "released": released,
        "download": (a.site.rstrip("/") + "/#download") if a.site else (release["releasePage"] or ""),
    }
    for name, data in [("release.json", release), ("version.json", version)]:
        with open(os.path.join(out, name), "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, indent=2)
            f.write("\n")
    if not a.no_web:
        for src, dst in [("release.json", WEB_DATA), ("version.json", WEB_VERSION)]:
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copyfile(os.path.join(out, src), dst)

    print(f"  {os.path.basename(mac_zip)}  {files[0]['size'] / 2**20:.1f} MB  sha256 {files[0]['sha256']}")
    print(f"  {os.path.basename(win_zip)}  {files[1]['size'] / 2**20:.1f} MB  sha256 {files[1]['sha256']}  ({win_count} files)")
    print(f"  checksums verified; written to {os.path.relpath(out, ROOT)}/" + ("" if a.no_web else " and copied to Web/"))
    if not repo:
        print("  no GitHub repo known (no origin remote, no --repo): download URLs are empty")


if __name__ == "__main__":
    main()
