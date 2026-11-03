using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace Saltline.Core
{
    /// <summary>
    /// Asks the release site which versions are still allowed online (version.json, written by
    /// Tools/package_release.py) and tells the menu whether this build is current, out of date, or
    /// too old to play online. A build whose protocol or version is below the published minimum
    /// cannot host, join or browse matches.
    ///
    /// This is a convenience for players, not protection: offline, or with the URL blocked, the check
    /// fails open, and a modified game can skip it. The protocol check in ConnectionGate is what
    /// keeps incompatible builds apart.
    ///
    ///   -updateurl &lt;url&gt;   read the manifest from another URL (https or file://, for tests)
    ///   -noupdatecheck        skip the check
    /// </summary>
    public static class UpdateCheck
    {
        public enum Status { NotStarted, NotConfigured, Checking, UpToDate, UpdateAvailable, UpdateRequired, Failed }

        /// <summary>The fields of version.json.</summary>
        [Serializable]
        public sealed class Manifest
        {
            public string game;
            public string latest;
            public int protocol;
            public int minimumProtocol;
            public string minimumVersion;
            public string released;
            public string download;
        }

        public readonly struct Result
        {
            public readonly Status Status;
            public readonly string Latest;
            /// <summary>An https URL for the download page, or null.</summary>
            public readonly string DownloadUrl;
            public readonly string Message;

            public Result(Status status, string latest, string downloadUrl, string message)
            {
                Status = status;
                Latest = latest;
                DownloadUrl = downloadUrl;
                Message = message;
            }
        }

        public const float TimeoutSeconds = 8f;

        public static Result Current { get; private set; } = new(Status.NotStarted, null, null, "");

        /// <summary>True when this build may not host, join or browse online matches.</summary>
        public static bool Blocking => Current.Status == Status.UpdateRequired;

        /// <summary>True until the check has an answer (or has been skipped).</summary>
        public static bool Pending => Current.Status is Status.NotStarted or Status.Checking;

        public static string Url => CommandLine.Get("updateurl", BuildInfo.UpdateManifestUrl);

        /// <summary>Starts the check from the boot scene (menu builds only).</summary>
        public static void Begin(MonoBehaviour host)
        {
            if (CommandLine.Has("noupdatecheck")) { Set(new Result(Status.NotConfigured, null, null, "Update check turned off")); return; }
            string url = Url;
            if (string.IsNullOrEmpty(url)) { Set(new Result(Status.NotConfigured, null, null, "")); return; }
            if (!IsAllowedSource(url)) { Set(new Result(Status.Failed, null, null, "Could not check for updates")); return; }
            Set(new Result(Status.Checking, null, null, "Checking for updates…"));
            host.StartCoroutine(Fetch(url));
        }

        static IEnumerator Fetch(string url)
        {
            using var req = UnityWebRequest.Get(url);
            req.timeout = Mathf.CeilToInt(TimeoutSeconds);
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.Log($"[SALTLINE] Update check: could not read {url}: {req.error}");
                Set(new Result(Status.Failed, null, null, "Could not check for updates"));
                yield break;
            }
            Set(Evaluate(Parse(req.downloadHandler.text), BuildInfo.Version, BuildInfo.NetProtocol));
        }

        static void Set(Result r)
        {
            Current = r;
            if (r.Status is Status.UpToDate or Status.UpdateAvailable or Status.UpdateRequired or Status.Failed)
                Debug.Log($"[SALTLINE] Update check: {r.Status} (this build v{BuildInfo.Version} protocol {BuildInfo.NetProtocol}" +
                          $"{(r.Latest != null ? $", latest v{r.Latest}" : "")})");
        }

        /// <summary>https for the real site; file:// so tests can use a local manifest.</summary>
        public static bool IsAllowedSource(string url) =>
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("file://", StringComparison.OrdinalIgnoreCase);

        public static Manifest Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonUtility.FromJson<Manifest>(json); }
            catch (ArgumentException) { return null; }
        }

        /// <summary>What the menu should do for a build of this version and protocol, given the manifest.</summary>
        public static Result Evaluate(Manifest m, string version, int protocol)
        {
            if (m == null || m.game != BuildInfo.GameName || !TryParseVersion(m.latest, out var latest) || m.protocol <= 0)
                return new Result(Status.Failed, null, null, "Could not check for updates");
            if (!TryParseVersion(version, out var mine))
                return new Result(Status.Failed, null, null, "Could not check for updates");

            string download = m.download != null && m.download.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? m.download : null;
            bool tooOldProtocol = m.minimumProtocol > 0 && protocol < m.minimumProtocol;
            bool tooOldVersion = TryParseVersion(m.minimumVersion, out var minimum) && Compare(mine, minimum) < 0;
            if (tooOldProtocol || tooOldVersion)
                return new Result(Status.UpdateRequired, m.latest, download,
                    $"This version ({version}) can no longer play online. Download SALTLINE {m.latest} to keep playing.");
            if (Compare(mine, latest) < 0)
                return new Result(Status.UpdateAvailable, m.latest, download, $"Version {m.latest} is available");
            return new Result(Status.UpToDate, m.latest, download, "");
        }

        /// <summary>Parses "major.minor.patch" (no pre-release suffix).</summary>
        public static bool TryParseVersion(string s, out (int major, int minor, int patch) v)
        {
            v = default;
            if (string.IsNullOrEmpty(s)) return false;
            var parts = s.Split('.');
            if (parts.Length != 3) return false;
            if (!int.TryParse(parts[0], System.Globalization.NumberStyles.None, null, out int a) ||
                !int.TryParse(parts[1], System.Globalization.NumberStyles.None, null, out int b) ||
                !int.TryParse(parts[2], System.Globalization.NumberStyles.None, null, out int c)) return false;
            v = (a, b, c);
            return true;
        }

        public static int Compare((int major, int minor, int patch) a, (int major, int minor, int patch) b) =>
            a.major != b.major ? a.major.CompareTo(b.major) : a.minor != b.minor ? a.minor.CompareTo(b.minor) : a.patch.CompareTo(b.patch);
    }

    /// <summary>Thrown when an online action is attempted by a build the update check found too old.</summary>
    public sealed class UpdateRequiredException : Exception
    {
        public UpdateRequiredException(string message) : base(message) { }
    }
}
