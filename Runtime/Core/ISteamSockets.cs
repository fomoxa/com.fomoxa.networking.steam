using System;

namespace Fomoxa.Networking.Steam
{
    internal enum SteamConnectionState
    {
        None,
        Connecting,
        FindingRoute,
        Connected,
        ClosedByPeer,
        ProblemDetectedLocally,
    }

    internal enum SteamSendResult
    {
        Sent,
        LimitExceeded,
        NoConnection,
        Failed,
    }

    internal readonly struct SteamConnectionChange
    {
        public SteamConnectionChange(uint connection, uint listenSocket, SteamConnectionState state, ulong remoteSteamId)
        {
            Connection = connection;
            ListenSocket = listenSocket;
            State = state;
            RemoteSteamId = remoteSteamId;
        }

        public uint Connection { get; }

        public uint ListenSocket { get; }

        public SteamConnectionState State { get; }

        public ulong RemoteSteamId { get; }

        public bool IsClosed => State == SteamConnectionState.ClosedByPeer || State == SteamConnectionState.ProblemDetectedLocally;
    }

    internal interface ISteamSockets
    {
        bool IsInitialized { get; }

        void Watch();

        void Unwatch();

        bool TryTakeChange(out SteamConnectionChange change);

        uint CreateListenSocket(int virtualPort);

        void CloseListenSocket(uint listenSocket);

        uint Connect(ulong steamId, int virtualPort);

        bool Accept(uint connection);

        void Close(uint connection, bool linger);

        SteamSendResult SendUnreliable(uint connection, ReadOnlySpan<byte> bytes);

        byte[] Receive(uint connection);
    }
}
