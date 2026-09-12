// Minimal game boundary for exercising the production RPC state machine without Unity/Wine.

using System;
using System.Collections.Generic;
using System.IO;

internal sealed class ZPackage
{
    private readonly MemoryStream _stream = new MemoryStream();
    public int Size() => (int)_stream.Length;
    public void Rewind() => _stream.Position = 0;
    public void Write(int value) => new BinaryWriter(_stream).Write(value);
    public void Write(long value) => new BinaryWriter(_stream).Write(value);
    public void Write(string value) => new BinaryWriter(_stream).Write(value);
    public int ReadInt() => new BinaryReader(_stream).ReadInt32();
    public long ReadLong() => new BinaryReader(_stream).ReadInt64();
    public string ReadString() => new BinaryReader(_stream).ReadString();
}

internal sealed class ZRpc
{
    private Action<ZRpc, ZPackage> _handler;
    public readonly List<ZPackage> Sent = new List<ZPackage>();

    public void Register<T>(string name, Action<ZRpc, T> handler) =>
        _handler = (rpc, package) => handler(rpc, (T)(object)package);

    public void Invoke(string name, params object[] args) => Sent.Add((ZPackage)args[0]);

    public void Deliver(ZPackage package)
    {
        package.Rewind();
        _handler(this, package);
    }
}

internal sealed class ZNetPeer
{
    public ZRpc m_rpc = new ZRpc();
    public long m_playerID;
    public long m_uid;
    public ZDOID m_characterID;
    public bool IsReady() => true;
}

internal readonly struct ZDOID
{
    internal readonly long Value;

    internal ZDOID(long value)
    {
        Value = value;
    }

    public static readonly ZDOID None = new ZDOID(0);
    public static bool operator ==(ZDOID a, ZDOID b) => a.Value == b.Value;
    public static bool operator !=(ZDOID a, ZDOID b) => a.Value != b.Value;
    public override bool Equals(object other) => other is ZDOID id && id == this;
    public override int GetHashCode() => Value.GetHashCode();
}

internal static class ZDOVars
{
    public static readonly int s_playerID = 1;
}

internal sealed class ZDO
{
    internal long Owner, CharacterId;
    public long GetOwner() => Owner;
    public long GetLong(int key, long fallback) => CharacterId;
}

internal sealed class ZDOMan
{
    public static ZDOMan instance = new ZDOMan();
    internal readonly Dictionary<ZDOID, ZDO> Objects = new Dictionary<ZDOID, ZDO>();
    public ZDO GetZDO(ZDOID id) => Objects.TryGetValue(id, out var value) ? value : null;
}

internal sealed class ZNet
{
    public static ZNet instance;
    public bool Server = true;
    public readonly List<ZNetPeer> Peers = new List<ZNetPeer>();
    public bool IsServer() => Server;
    public bool IsDedicated() => Server;
    public List<ZNetPeer> GetPeers() => Peers;
    public ZNetPeer GetServerPeer() => Server || Peers.Count == 0 ? null : Peers[0];
}

internal sealed class Player
{
    public static Player m_localPlayer;
    public long Id;
    public long GetPlayerID() => Id;
    public FakeAvatar GetVrmInstance() => new FakeAvatar();
}

internal sealed class FakeAvatar
{
    public FakeSettings GetSettings() => new FakeSettings();
}

internal sealed class FakeSettings
{
    public bool AllowShare = true;
}

namespace EnhancedValheimVRM
{
    internal static class VrmController
    {
        internal static FakeAvatar FindSharingInstance(Player player) => player?.GetVrmInstance();
    }

    internal static class Settings
    {
        public static bool EnableVrmSharing = true, EnableSharingServer = true;
        public static bool LogLoadTiming = false;
        public static string VrmKey = "";
    }

    internal static class SharingPortDiscovery
    {
        public static bool TryGetServerPort(ZNet network, out int port)
        {
            port = 6067;
            return true;
        }
    }

    internal static class OutfitRpc
    {
        public static void SelectionReceived(long id)
        {
        }

        public static void ClientReady()
        {
        }
    }

    internal static class Logger
    {
        internal static void Log(string message) { }
        internal static void ResetOnce()
        {
        }

        internal static void LogOnce(string key, string message)
        {
        }
    }

    internal static class FileTransferController
    {
        public static string PlayerLabel(long id) => "player " + id;

        public static void ForgetCharacter(long id)
        {
        }

        public static void ResetConnection()
        {
        }
    }
}