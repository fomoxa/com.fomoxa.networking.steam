using System;
using System.Collections.Generic;

namespace Fomoxa.Networking.Steam.Tests
{
    internal sealed class FakeSteamNetwork
    {
        private readonly Dictionary<(ulong SteamId, int VirtualPort), Listening> listening = new Dictionary<(ulong, int), Listening>();
        private readonly Dictionary<uint, End> ends = new Dictionary<uint, End>();
        private uint nextHandle = 100;

        public FakeSteamSockets Join(ulong steamId) => new FakeSteamSockets(this, steamId);

        internal uint Listen(FakeSteamSockets sockets, int virtualPort)
        {
            uint handle = nextHandle++;
            listening[(sockets.SteamId, virtualPort)] = new Listening(sockets, handle);
            return handle;
        }

        internal void StopListening(FakeSteamSockets sockets, uint listenSocket)
        {
            foreach (KeyValuePair<(ulong SteamId, int VirtualPort), Listening> entry in new List<KeyValuePair<(ulong, int), Listening>>(listening))
            {
                if (entry.Value.Sockets == sockets && entry.Value.Handle == listenSocket)
                {
                    listening.Remove(entry.Key);
                }
            }
        }

        internal uint Connect(FakeSteamSockets sockets, ulong steamId, int virtualPort)
        {
            var local = new End(sockets, nextHandle++, 0, steamId);
            ends.Add(local.Handle, local);
            if (!listening.TryGetValue((steamId, virtualPort), out Listening host))
            {
                local.Closed = true;
                sockets.Raise(new SteamConnectionChange(local.Handle, 0, SteamConnectionState.ProblemDetectedLocally, steamId));
                return local.Handle;
            }

            var remote = new End(host.Sockets, nextHandle++, host.Handle, sockets.SteamId);
            ends.Add(remote.Handle, remote);
            local.Peer = remote;
            remote.Peer = local;
            host.Sockets.Raise(new SteamConnectionChange(remote.Handle, host.Handle, SteamConnectionState.Connecting, sockets.SteamId));
            return local.Handle;
        }

        internal bool Accept(FakeSteamSockets sockets, uint connection)
        {
            if (!ends.TryGetValue(connection, out End end) || end.Sockets != sockets || end.ListenSocket == 0 || end.Closed || end.Peer.Closed)
            {
                return false;
            }

            end.Connected = true;
            end.Peer.Connected = true;
            end.Sockets.Raise(new SteamConnectionChange(end.Handle, end.ListenSocket, SteamConnectionState.Connected, end.RemoteSteamId));
            end.Peer.Sockets.Raise(new SteamConnectionChange(end.Peer.Handle, 0, SteamConnectionState.Connected, end.Peer.RemoteSteamId));
            return true;
        }

        internal void Close(FakeSteamSockets sockets, uint connection)
        {
            if (!ends.TryGetValue(connection, out End end) || end.Sockets != sockets)
            {
                return;
            }

            ends.Remove(connection);
            end.Closed = true;
            End peer = end.Peer;
            if (peer != null && !peer.Closed)
            {
                peer.Closed = true;
                peer.Sockets.Raise(new SteamConnectionChange(peer.Handle, peer.ListenSocket, SteamConnectionState.ClosedByPeer, peer.RemoteSteamId));
            }
        }

        internal SteamSendResult Send(FakeSteamSockets sockets, uint connection, ReadOnlySpan<byte> bytes)
        {
            if (!ends.TryGetValue(connection, out End end) || end.Sockets != sockets || end.Closed || !end.Connected)
            {
                return SteamSendResult.NoConnection;
            }

            if (end.Peer.Inbox.Count >= sockets.QueuedMessageLimit)
            {
                return SteamSendResult.LimitExceeded;
            }

            end.Peer.Inbox.Enqueue(bytes.ToArray());
            return SteamSendResult.Sent;
        }

        internal byte[] Receive(FakeSteamSockets sockets, uint connection)
        {
            if (ends.TryGetValue(connection, out End end) && end.Sockets == sockets && end.Inbox.Count > 0)
            {
                return end.Inbox.Dequeue();
            }

            return null;
        }

        private sealed class Listening
        {
            public Listening(FakeSteamSockets sockets, uint handle)
            {
                Sockets = sockets;
                Handle = handle;
            }

            public FakeSteamSockets Sockets { get; }

            public uint Handle { get; }
        }

        private sealed class End
        {
            public End(FakeSteamSockets sockets, uint handle, uint listenSocket, ulong remoteSteamId)
            {
                Sockets = sockets;
                Handle = handle;
                ListenSocket = listenSocket;
                RemoteSteamId = remoteSteamId;
            }

            public FakeSteamSockets Sockets { get; }

            public uint Handle { get; }

            public uint ListenSocket { get; }

            public ulong RemoteSteamId { get; }

            public End Peer { get; set; }

            public bool Connected { get; set; }

            public bool Closed { get; set; }

            public Queue<byte[]> Inbox { get; } = new Queue<byte[]>();
        }
    }

    internal sealed class FakeSteamSockets : ISteamSockets
    {
        private readonly FakeSteamNetwork network;
        private readonly Queue<SteamConnectionChange> changes = new Queue<SteamConnectionChange>();

        public FakeSteamSockets(FakeSteamNetwork network, ulong steamId)
        {
            this.network = network;
            SteamId = steamId;
        }

        public ulong SteamId { get; }

        public bool IsInitialized { get; set; } = true;

        public int Watching { get; private set; }

        public int WatchCalls { get; private set; }

        public int QueuedMessageLimit { get; set; } = int.MaxValue;

        public List<uint> Closed { get; } = new List<uint>();

        public List<uint> ClosedListenSockets { get; } = new List<uint>();

        public List<bool> Lingers { get; } = new List<bool>();

        public bool RefuseAccept { get; set; }

        public void Watch()
        {
            Watching++;
            WatchCalls++;
        }

        public void Unwatch()
        {
            Watching--;
        }

        public bool TryTakeChange(out SteamConnectionChange change)
        {
            if (changes.Count > 0)
            {
                change = changes.Dequeue();
                return true;
            }

            change = default;
            return false;
        }

        public uint CreateListenSocket(int virtualPort) => network.Listen(this, virtualPort);

        public void CloseListenSocket(uint listenSocket)
        {
            ClosedListenSockets.Add(listenSocket);
            network.StopListening(this, listenSocket);
        }

        public uint Connect(ulong steamId, int virtualPort) => network.Connect(this, steamId, virtualPort);

        public bool Accept(uint connection) => !RefuseAccept && network.Accept(this, connection);

        public void Close(uint connection, bool linger)
        {
            Closed.Add(connection);
            Lingers.Add(linger);
            network.Close(this, connection);
        }

        public SteamSendResult SendUnreliable(uint connection, ReadOnlySpan<byte> bytes) => network.Send(this, connection, bytes);

        public byte[] Receive(uint connection) => network.Receive(this, connection);

        internal void Raise(SteamConnectionChange change)
        {
            if (Watching > 0)
            {
                changes.Enqueue(change);
            }
        }
    }
}
