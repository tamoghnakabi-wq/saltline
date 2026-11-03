# Releases, versions and distribution

How a release is versioned, packaged, checked and published.

- Repository: https://github.com/tamoghnakabi-wq/saltline (public)
- Releases: https://github.com/tamoghnakabi-wq/saltline/releases
- Website: https://saltline-five.vercel.app (a Vercel project named `saltline`, built from `Web/`, free Hobby plan)

The releases published so far are listed under [Published releases](#published-releases).

## Versions

`Assets/_Saltline/Scripts/Runtime/Core/BuildInfo.cs` holds both numbers every build carries:

- **`Version`** (`0.1.0`): the game version, shown in the menu footer and used in file names and release tags.
  Bump it for every release.
- **`NetProtocol`** (`4`): bump it whenever a network message, RPC or synchronized struct changes. Two builds can
  play together only if their protocols are equal:
  - the session browser only lists sessions with the player's protocol;
  - the host's connection approval rejects a client with a different protocol and tells it to update.

## Update check

On launch, the main menu reads a small JSON file (`version.json`) from `BuildInfo.UpdateManifestUrl`:

```json
{ "game": "SALTLINE", "latest": "0.1.0", "protocol": 4, "minimumProtocol": 4, "minimumVersion": "",
  "released": "2026-11-03", "download": "https://…/#download" }
```

| The running build | Result in the menu |
|---|---|
| protocol below `minimumProtocol`, or version below `minimumVersion` | **Update required.** Host, Join and Browse are disabled and the menu shows a Download button. |
| older than `latest`, still allowed | Update available: a note in the footer, play is not blocked. |
| same as or newer than `latest` | Nothing shown. |
| the file cannot be fetched or read | "Could not check for updates" in the footer; play is not blocked. |

When a release changes the protocol, publish its `version.json` with `minimumProtocol` set to the new protocol
(the packaging script's default), so older builds are told to update instead of finding an empty session list.

This check is a convenience, not a security boundary. Someone offline, or blocking the URL, is not blocked, and
a modified game can skip the check. What actually keeps incompatible builds apart is the protocol check at
connection approval.

`UpdateManifestUrl` is `https://saltline-five.vercel.app/version.json`. An empty address turns the check off
(it reports "not configured"), and the packaging script refuses such builds for a release. For tests,
`-updateurl <url>` overrides it (`file://` works), and `-noupdatecheck` turns it off. Only `https://` and
`file://` addresses are read, and the Download button only opens `https://` links. The request times out after
8 seconds.

To test the menu's reaction, the UI test has an update mode. It waits for the check, saves a picture of the
menu, and checks that Host, Join and Browse (the buttons and the session calls behind them) are blocked exactly
when an update is required:

```bash
SALTLINE.app/Contents/MacOS/SALTLINE -batchmode -capture out -uitest update -testout out/result.txt \
  -updateurl file:///path/to/version.json -expectupdate UpdateRequired
```

`-expectupdate` is one of `UpdateRequired`, `UpdateAvailable`, `UpToDate`, `Failed` or `NotConfigured`.

## Packaging

Build both players from a clean, committed tree, then package them:

```bash
Unity -batchmode -quit -projectPath . -executeMethod Saltline.EditorTools.SaltlineBuild.BuildMac
Unity -batchmode -quit -projectPath . -executeMethod Saltline.EditorTools.SaltlineBuild.BuildWindows
python3 Tools/package_release.py --repo OWNER/REPO --site https://SITE
```

Each build writes `SALTLINE.buildinfo.json` next to its output (version, protocol, commit, release or
development). The script refuses to package if either build is a development build, was built from another
commit or another version, or from uncommitted changes, or if the update URL is empty. It writes
`Release/v<version>/`:

| File | Contents |
|---|---|
| `SALTLINE-<version>-macOS-arm64.zip` | the .app, zipped with `ditto` so its signature survives; the script unzips it again and verifies the signature |
| `SALTLINE-<version>-Windows-x64.zip` | the game folder without Unity's `*_DoNotShip` debug symbols; read back and checked entry by entry |
| `SHA256SUMS.txt` | checksums; the script checks them with `shasum -a 256 -c` |
| `release.json` | everything the website's download section shows |
| `version.json` | what the update check reads |

`release.json` and `version.json` are also copied into `Web/`, so the site is rebuilt from the same data.

Players verify a download with `shasum -a 256 <file>` (macOS) or `Get-FileHash <file>` (PowerShell) and compare
it with the site or `SHA256SUMS.txt`.

## macOS: signing, Gatekeeper and notarization

**Current state:** Unity signs the Apple-silicon app ad hoc (`codesign -dv`: `Signature=adhoc`,
`TeamIdentifier=not set`). The signature is valid (`codesign --verify --deep --strict` passes, also after a
`ditto` zip and unzip), which is what lets an arm64 app run at all. It is not a Developer ID signature and the
app is not notarized.

**Tested on 2026-11-03 (macOS 27.2):** a copy unzipped from the release zip and given the quarantine flag a
browser adds to downloads:

- `spctl --assess --type execute`: `rejected`, with and without the quarantine flag.
- `syspolicy_check distribution`: fails with "Notary Ticket Missing" (fatal) and "Adhoc Signed App" (warning).

So Gatekeeper blocks the downloaded app the first time it is opened. Since macOS 15 (Sequoia), Control-click >
Open no longer overrides this. The player has to open System Settings > Privacy & Security, choose Open Anyway
next to the message about SALTLINE, and confirm with their password. The approval is per app and needed once.
The alternative is `xattr -dr com.apple.quarantine /Applications/SALTLINE.app` in Terminal. Both are on the
website. The exact dialog on this machine was not captured: opening the quarantined copy would have left a
Gatekeeper alert on the screen.

**Removing the warning needs notarization, and notarization needs a paid membership.** A Developer ID
Application certificate comes only with the Apple Developer Program (US$99 a year; individuals cannot get the
fee waived). With it, a release would be: sign with the Developer ID certificate, the hardened runtime and a
secure timestamp; submit the zip with `xcrun notarytool submit --wait`; staple the ticket with
`xcrun stapler staple`; zip again. A free Apple ID only gives development certificates, which Gatekeeper treats
the same as ad hoc for downloads. This has not been done, because the business constraints rule out paid
accounts. It needs the owner's approval.

## Windows: code signing and SmartScreen

**Current state:** `SALTLINE.exe` and Unity's DLLs are not Authenticode-signed. Windows SmartScreen will likely
show "Windows protected your PC" for a downloaded unsigned executable; the player chooses More info > Run anyway.
This has not been seen on a Windows machine, because none was available.

Options to sign, none taken:

- **A commercial code-signing certificate** (OV or EV), or a cloud signing service: all paid.
- **SignPath Foundation** signs free, but only for open-source projects with an OSI licence and no proprietary
  components. SALTLINE ships Unity's proprietary player runtime and Mixamo animations, so it does not qualify.

## Publishing

Each release needs the owner's go-ahead. The steps, in order:

1. Bump `Version` (and `NetProtocol` if the network changed) in `BuildInfo.cs`, write
   `docs/releases/<version>.md`, commit, and push.
2. Build both players from that commit and package them:
   `python3 Tools/package_release.py --site https://saltline-five.vercel.app`. The repository is taken from the
   `origin` remote; `--repo OWNER/REPO` overrides it.
3. Create the release at the commit the players were built from:

   ```bash
   gh release create v<version> Release/v<version>/*.zip Release/v<version>/SHA256SUMS.txt \
     --target <commit> --title "SALTLINE <version>" --notes-file docs/releases/<version>.md
   ```

4. Commit the updated `Web/src/data/release.json` and `Web/public/version.json`, and push.
5. Deploy the site. The Vercel project is not connected to the repository, so pushing does not deploy it. From
   `Web/`, with the Vercel CLI signed in to the account that owns the project:

   ```bash
   vercel link --project saltline   # once per checkout
   vercel deploy --prod
   ```

   `Web/vercel.json` sets the framework (Next.js), `npm ci`, and `npm run check && npm run build`, so a release
   whose data does not match fails the deployment. `vercel link` writes `Web/.env.local` with a short-lived
   Vercel token: it is ignored by Git and listed in `Web/.vercelignore`, and must never be committed.
6. Check that `/version.json` and the download links work, and that the checksums on the site match the files on
   the release.
7. Start the published build and confirm the menu shows no update notice. For a release that changes the
   protocol, also start the previous release and confirm its menu blocks online play.

## Published releases

| Version | Date | Commit | Files |
|---|---|---|---|
| 0.1.0 | 2026-11-03 | `83c0d07` | [release page](https://github.com/tamoghnakabi-wq/saltline/releases/tag/v0.1.0): `SALTLINE-0.1.0-macOS-arm64.zip`, `SALTLINE-0.1.0-Windows-x64.zip`, `SHA256SUMS.txt` |
