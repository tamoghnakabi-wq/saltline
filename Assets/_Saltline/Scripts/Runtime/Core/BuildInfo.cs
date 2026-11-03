namespace Saltline.Core
{
    /// <summary>
    /// Version constants shared by every build. Bump <see cref="NetProtocol"/> whenever
    /// any network message, RPC signature or synchronized struct layout changes, so
    /// incompatible builds are rejected at connection approval instead of desyncing.
    /// </summary>
    public static class BuildInfo
    {
        public const string GameName = "SALTLINE";

        /// <summary>Semantic game version shown in menus, release names and the update check.</summary>
        public const string Version = "0.1.0";
        public const int NetProtocol = 4; // 3: match flow (MatchDirector on the session object); 4: ShotFx.PelletHits

        /// <summary>
        /// The release site's version.json, read by <see cref="UpdateCheck"/> on launch. Empty until the site
        /// is published (the check then does nothing); must be https. See docs/DISTRIBUTION.md.
        /// </summary>
        public const string UpdateManifestUrl = "";
        public const string SessionType = "saltline_tdm";
        public const int MaxPlayers = 8;
        public const int MaxPerTeam = 4;

        // Indexed session property keys (used for browser filtering).
        public const string PropMode = "mode";
        public const string PropMap = "map";
        public const string PropPhase = "phase";
        public const string PropProtocol = "proto";
        public const string PropHostName = "host";
    }
}
