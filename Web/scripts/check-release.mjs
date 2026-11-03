// Checks that the release data the site is built from is consistent: release.json (download section)
// and public/version.json (read by the game's update check) must describe the same version.
import { readFileSync } from "node:fs";

const release = JSON.parse(readFileSync(new URL("../src/data/release.json", import.meta.url), "utf8"));
const version = JSON.parse(readFileSync(new URL("../public/version.json", import.meta.url), "utf8"));
const problems = [];
const semver = /^\d+\.\d+\.\d+$/;

if (!semver.test(release.version)) problems.push(`release.json version '${release.version}' is not X.Y.Z`);
if (version.latest !== release.version) problems.push(`version.json latest ${version.latest} != release.json ${release.version}`);
if (version.protocol !== release.protocol) problems.push(`version.json protocol ${version.protocol} != release.json ${release.protocol}`);
if (version.minimumProtocol > version.protocol) problems.push("version.json minimumProtocol is newer than its protocol");
if (version.minimumVersion && !semver.test(version.minimumVersion)) problems.push("version.json minimumVersion is not X.Y.Z");
for (const f of release.files) {
  if (!/^[0-9a-f]{64}$/.test(f.sha256)) problems.push(`${f.name}: sha256 is not 64 hex characters`);
  if (!(f.size > 0)) problems.push(`${f.name}: size missing`);
  if (f.url && !f.url.startsWith("https://")) problems.push(`${f.name}: download URL is not https`);
}
if (version.download && !version.download.startsWith("https://")) problems.push("version.json download is not https");

if (problems.length) {
  console.error("Release data problems:\n- " + problems.join("\n- "));
  process.exit(1);
}
console.log(`Release data OK: ${release.game} ${release.version}, protocol ${release.protocol}, ${release.files.length} files`);
