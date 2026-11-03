using System.IO;
using NUnit.Framework;
using Saltline.Core;
using UnityEngine;
using static Saltline.Core.UpdateCheck;

namespace Saltline.Tests
{
    /// <summary>
    /// The update check's decisions (which builds are blocked, which are told an update exists) and the
    /// version.json the website ships.
    /// </summary>
    public class UpdateCheckTests
    {
        static Manifest M(string latest = "0.2.0", int protocol = 5, int minProtocol = 5, string minVersion = "",
                          string download = "https://example.com/#download") =>
            new() { game = BuildInfo.GameName, latest = latest, protocol = protocol, minimumProtocol = minProtocol,
                    minimumVersion = minVersion, released = "2026-11-03", download = download };

        [TestCase("0.1.0", 0, 1, 0)]
        [TestCase("10.20.30", 10, 20, 30)]
        public void ParsesVersions(string s, int a, int b, int c)
        {
            Assert.IsTrue(TryParseVersion(s, out var v));
            Assert.AreEqual((a, b, c), v);
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("1.2")]
        [TestCase("1.2.3.4")]
        [TestCase("1.2.x")]
        [TestCase("v1.2.3")]
        [TestCase("1.2.3-beta")]
        [TestCase("-1.2.3")]
        [TestCase(" 1.2.3")]
        public void RejectsBadVersions(string s) => Assert.IsFalse(TryParseVersion(s, out _));

        [Test]
        public void ComparesVersionsNumerically()
        {
            TryParseVersion("0.9.0", out var a);
            TryParseVersion("0.10.0", out var b);
            TryParseVersion("1.0.0", out var c);
            Assert.Less(Compare(a, b), 0, "0.9.0 < 0.10.0 (numbers, not text)");
            Assert.Less(Compare(b, c), 0);
            Assert.AreEqual(0, Compare(c, c));
            Assert.Greater(Compare(c, a), 0);
        }

        [Test]
        public void OlderProtocolIsRequiredToUpdate()
        {
            var r = Evaluate(M(), "0.1.0", 4);
            Assert.AreEqual(Status.UpdateRequired, r.Status);
            Assert.AreEqual("0.2.0", r.Latest);
            Assert.AreEqual("https://example.com/#download", r.DownloadUrl);
            StringAssert.Contains("0.2.0", r.Message);
        }

        [Test]
        public void VersionBelowMinimumIsRequiredToUpdate()
        {
            var r = Evaluate(M(protocol: 4, minProtocol: 4, minVersion: "0.1.5"), "0.1.0", 4);
            Assert.AreEqual(Status.UpdateRequired, r.Status);
        }

        [Test]
        public void SameProtocolOlderVersionIsToldOnly()
        {
            var r = Evaluate(M(protocol: 4, minProtocol: 4), "0.1.0", 4);
            Assert.AreEqual(Status.UpdateAvailable, r.Status);
            Assert.AreEqual("0.2.0", r.Latest);
        }

        [Test]
        public void CurrentOrNewerBuildIsUpToDate()
        {
            Assert.AreEqual(Status.UpToDate, Evaluate(M("0.1.0", 4, 4), "0.1.0", 4).Status);
            Assert.AreEqual(Status.UpToDate, Evaluate(M("0.1.0", 4, 4), "0.2.0", 5).Status, "a build newer than the site");
        }

        [Test]
        public void NoMinimumMeansNothingIsBlocked()
        {
            Assert.AreEqual(Status.UpdateAvailable, Evaluate(M(minProtocol: 0), "0.1.0", 1).Status);
        }

        [Test]
        public void BadManifestsFailOpen()
        {
            Assert.AreEqual(Status.Failed, Evaluate(null, "0.1.0", 4).Status);
            var other = M(); other.game = "SOMETHING ELSE";
            Assert.AreEqual(Status.Failed, Evaluate(other, "0.1.0", 4).Status);
            Assert.AreEqual(Status.Failed, Evaluate(M(latest: "soon"), "0.1.0", 4).Status);
            Assert.AreEqual(Status.Failed, Evaluate(M(protocol: 0), "0.1.0", 4).Status);
            Assert.IsNull(Parse(""));
            Assert.IsNull(Parse("not json"));
            Assert.AreEqual(Status.Failed, Evaluate(Parse("{}"), "0.1.0", 4).Status);
        }

        [Test]
        public void OnlyHttpsDownloadLinksAreKept()
        {
            Assert.IsNull(Evaluate(M(download: "http://example.com"), "0.1.0", 4).DownloadUrl);
            Assert.IsNull(Evaluate(M(download: "file:///Applications"), "0.1.0", 4).DownloadUrl);
            Assert.IsNull(Evaluate(M(download: "javascript:alert(1)"), "0.1.0", 4).DownloadUrl);
            Assert.IsNull(Evaluate(M(download: ""), "0.1.0", 4).DownloadUrl);
        }

        [Test]
        public void ManifestSourcesAreHttpsOrLocalFiles()
        {
            Assert.IsTrue(IsAllowedSource("https://example.com/version.json"));
            Assert.IsTrue(IsAllowedSource("file:///tmp/version.json"));
            Assert.IsFalse(IsAllowedSource("http://example.com/version.json"));
            Assert.IsFalse(IsAllowedSource("example.com/version.json"));
        }

        [Test]
        public void BuiltInUpdateUrlIsEmptyOrHttps()
        {
            string url = BuildInfo.UpdateManifestUrl;
            Assert.IsTrue(url == "" || url.StartsWith("https://"), $"UpdateManifestUrl must be empty or https: '{url}'");
        }

        /// <summary>The site's version.json must parse and must never ask this build to update to itself or block it.</summary>
        [Test]
        public void WebsiteManifestMatchesThisBuild()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Web", "public", "version.json"));
            Assert.IsTrue(File.Exists(path), path);
            var m = Parse(File.ReadAllText(path));
            Assert.IsNotNull(m, "version.json does not parse");
            Assert.AreEqual(BuildInfo.GameName, m.game);
            Assert.IsTrue(TryParseVersion(m.latest, out var latest), $"latest '{m.latest}'");
            TryParseVersion(BuildInfo.Version, out var mine);
            Assert.LessOrEqual(Compare(latest, mine), 0, "the site names a version newer than this source tree");
            Assert.LessOrEqual(m.protocol, BuildInfo.NetProtocol);
            var r = Evaluate(m, BuildInfo.Version, BuildInfo.NetProtocol);
            Assert.AreEqual(Status.UpToDate, r.Status, "this build, checked against the site's own file");
        }
    }
}
