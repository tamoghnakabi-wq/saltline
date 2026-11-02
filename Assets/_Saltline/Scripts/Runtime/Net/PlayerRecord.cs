using System;
using Unity.Collections;
using Unity.Netcode;

namespace Saltline.Net
{
    public enum Team : byte
    {
        None = 0,
        Red = 1,
        Blue = 2,
    }

    public enum ClientPlatform : byte
    {
        Unknown = 0,
        Windows = 1,
        MacOS = 2,
        Linux = 3,
        Editor = 4,
    }

    /// <summary>
    /// Authoritative per-player match record. Only the host writes these; clients read them
    /// (roster, scoreboard, results). Kept small because it is replicated to everyone.
    /// </summary>
    public struct PlayerRecord : INetworkSerializable, IEquatable<PlayerRecord>
    {
        public ulong ClientId;
        public FixedString32Bytes Name;
        public FixedString64Bytes AuthId;
        public Team Team;
        public bool Ready;
        public bool IsHost;
        public ClientPlatform Platform;
        public short Kills;
        public short Deaths;
        public short Assists;
        public int Score;
        public int DamageDealt;
        public ushort PingMs;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref ClientId);
            s.SerializeValue(ref Name);
            s.SerializeValue(ref AuthId);
            s.SerializeValue(ref Team);
            s.SerializeValue(ref Ready);
            s.SerializeValue(ref IsHost);
            s.SerializeValue(ref Platform);
            s.SerializeValue(ref Kills);
            s.SerializeValue(ref Deaths);
            s.SerializeValue(ref Assists);
            s.SerializeValue(ref Score);
            s.SerializeValue(ref DamageDealt);
            s.SerializeValue(ref PingMs);
        }

        public bool Equals(PlayerRecord o) =>
            ClientId == o.ClientId && Name.Equals(o.Name) && AuthId.Equals(o.AuthId) && Team == o.Team &&
            Ready == o.Ready && IsHost == o.IsHost && Platform == o.Platform && Kills == o.Kills &&
            Deaths == o.Deaths && Assists == o.Assists && Score == o.Score && DamageDealt == o.DamageDealt &&
            PingMs == o.PingMs;

        public override bool Equals(object obj) => obj is PlayerRecord o && Equals(o);
        public override int GetHashCode() => ClientId.GetHashCode();

        public static ClientPlatform LocalPlatform()
        {
#if UNITY_EDITOR
            return ClientPlatform.Editor;
#elif UNITY_STANDALONE_WIN
            return ClientPlatform.Windows;
#elif UNITY_STANDALONE_OSX
            return ClientPlatform.MacOS;
#elif UNITY_STANDALONE_LINUX
            return ClientPlatform.Linux;
#else
            return ClientPlatform.Unknown;
#endif
        }
    }
}
