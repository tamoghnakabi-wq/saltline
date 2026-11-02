using System;
using System.Collections.Generic;
using Saltline.Core;
using Unity.Netcode;
using UnityEngine;

namespace Saltline.Match
{
    /// <summary>Menu → Lobby → Loading → Warmup → Live → Results → Lobby.</summary>
    public enum MatchPhase : byte
    {
        Lobby = 0,
        Loading = 1,
        Warmup = 2,
        Live = 3,
        Results = 4,
    }

    public enum MatchEndReason : byte
    {
        None = 0,
        ScoreLimit = 1,
        TimeLimit = 2,
        HostEnded = 3,
    }

    /// <summary>Team Deathmatch rules, chosen by the host and replicated to everyone.</summary>
    [Serializable]
    public struct MatchRules : INetworkSerializeByMemcpy, IEquatable<MatchRules>
    {
        public float TimeLimit;   // seconds of live play
        public short ScoreLimit;  // team kills needed to win
        public float Warmup;      // seconds of free play before the match starts
        public float Results;     // seconds on the results screen before returning to the lobby

        public static MatchRules Default => new() { TimeLimit = 600f, ScoreLimit = 50, Warmup = 15f, Results = 15f };

        /// <summary>Custom-match overrides for testing: -timelimit -scorelimit -warmup -resultstime.</summary>
        public static MatchRules FromCommandLine()
        {
            var r = Default;
            r.TimeLimit = Mathf.Clamp(CommandLine.GetFloat("timelimit", r.TimeLimit), 10f, 3600f);
            r.ScoreLimit = (short)Mathf.Clamp(CommandLine.GetFloat("scorelimit", r.ScoreLimit), 1f, 500f);
            r.Warmup = Mathf.Clamp(CommandLine.GetFloat("warmup", r.Warmup), 0f, 120f);
            r.Results = Mathf.Clamp(CommandLine.GetFloat("resultstime", r.Results), 3f, 120f);
            return r;
        }

        public bool Equals(MatchRules o) => TimeLimit == o.TimeLimit && ScoreLimit == o.ScoreLimit && Warmup == o.Warmup && Results == o.Results;
        public override bool Equals(object obj) => obj is MatchRules o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(TimeLimit, ScoreLimit, Warmup, Results);
    }

    /// <summary>Playable maps. A map is offered only if its scene is in the build.</summary>
    public static class MapCatalog
    {
        public readonly struct MapInfo
        {
            public readonly string Id, Scene, Name, Blurb;
            public MapInfo(string id, string scene, string name, string blurb) { Id = id; Scene = scene; Name = name; Blurb = blurb; }
        }

        public static readonly MapInfo[] All =
        {
            new("kappa", "Kappa", "Kappa Saltworks", "A salt evaporation plant on a cliff coast. Three lanes, a raised pump house, and flanks through the brine tanks."),
            new("arena", "Arena", "Proving Ground", "Open test range for warming up and trying weapons."),
        };

        public static IEnumerable<MapInfo> Available()
        {
            foreach (var m in All)
                if (Application.CanStreamedLevelBeLoaded(m.Scene)) yield return m;
        }

        public static bool TryGet(string id, out MapInfo map)
        {
            foreach (var m in All)
                if (string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) { map = m; return true; }
            map = default;
            return false;
        }

        /// <summary>The preferred map that is actually in this build.</summary>
        public static string DefaultId()
        {
            foreach (var m in Available()) return m.Id;
            return "arena";
        }
    }
}
