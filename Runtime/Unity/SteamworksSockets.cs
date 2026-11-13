using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Fomoxa.Networking.Steam;
using Steamworks;

namespace Fomoxa.Unity.Steam
{
    internal sealed class SteamworksSockets : ISteamSockets
    {
        private static readonly SteamNetworkingConfigValue_t[] NoOptions = Array.Empty<SteamNetworkingConfigValue_t>();

        private readonly ConcurrentQueue<SteamConnectionChange> changes = new ConcurrentQueue<SteamConnectionChange>();
        private readonly IntPtr[] receivedMessage = new IntPtr[1];
        private Callback<SteamNetConnectionStatusChangedCallback_t> statusChanged;

        public bool IsInitialized
        {
            get
            {
                try
                {
                    SteamUser.GetSteamID();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }

        public void Watch()
        {
            statusChanged ??= Callback<SteamNetConnectionStatusChangedCallback_t>.Create(Enqueue);
        }

        public void Unwatch()
        {
            statusChanged?.Dispose();
            statusChanged = null;
            while (changes.TryDequeue(out _))
            {
            }
        }

        public bool TryTakeChange(out SteamConnectionChange change) => changes.TryDequeue(out change);

        public uint CreateListenSocket(int virtualPort) =>
            SteamNetworkingSockets.CreateListenSocketP2P(virtualPort, 0, NoOptions).m_HSteamListenSocket;

        public void CloseListenSocket(uint listenSocket) =>
            SteamNetworkingSockets.CloseListenSocket(new HSteamListenSocket(listenSocket));

        public uint Connect(ulong steamId, int virtualPort)
        {
            var remote = new SteamNetworkingIdentity();
            remote.SetSteamID64(steamId);
            return SteamNetworkingSockets.ConnectP2P(ref remote, virtualPort, 0, NoOptions).m_HSteamNetConnection;
        }

        public bool Accept(uint connection) =>
            SteamNetworkingSockets.AcceptConnection(new HSteamNetConnection(connection)) == EResult.k_EResultOK;

        public void Close(uint connection, bool linger) =>
            SteamNetworkingSockets.CloseConnection(
                new HSteamNetConnection(connection),
                (int)ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_App_Generic,
                string.Empty,
                linger);

        public unsafe SteamSendResult SendUnreliable(uint connection, ReadOnlySpan<byte> bytes)
        {
            EResult result;
            fixed (byte* data = bytes)
            {
                result = SteamNetworkingSockets.SendMessageToConnection(
                    new HSteamNetConnection(connection),
                    (IntPtr)data,
                    (uint)bytes.Length,
                    Constants.k_nSteamNetworkingSend_UnreliableNoNagle,
                    out _);
            }

            return result switch
            {
                EResult.k_EResultOK => SteamSendResult.Sent,
                EResult.k_EResultLimitExceeded => SteamSendResult.LimitExceeded,
                EResult.k_EResultNoConnection => SteamSendResult.NoConnection,
                _ => SteamSendResult.Failed,
            };
        }

        public byte[] Receive(uint connection)
        {
            if (SteamNetworkingSockets.ReceiveMessagesOnConnection(new HSteamNetConnection(connection), receivedMessage, 1) < 1)
            {
                return null;
            }

            IntPtr pointer = receivedMessage[0];
            try
            {
                SteamNetworkingMessage_t message = SteamNetworkingMessage_t.FromIntPtr(pointer);
                var bytes = new byte[message.m_cbSize];
                Marshal.Copy(message.m_pData, bytes, 0, message.m_cbSize);
                return bytes;
            }
            finally
            {
                SteamNetworkingMessage_t.Release(pointer);
            }
        }

        private void Enqueue(SteamNetConnectionStatusChangedCallback_t status)
        {
            SteamNetworkingIdentity remote = status.m_info.m_identityRemote;
            changes.Enqueue(new SteamConnectionChange(
                status.m_hConn.m_HSteamNetConnection,
                status.m_info.m_hListenSocket.m_HSteamListenSocket,
                StateOf(status.m_info.m_eState),
                remote.GetSteamID64()));
        }

        private static SteamConnectionState StateOf(ESteamNetworkingConnectionState state) => state switch
        {
            ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting => SteamConnectionState.Connecting,
            ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_FindingRoute => SteamConnectionState.FindingRoute,
            ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected => SteamConnectionState.Connected,
            ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer => SteamConnectionState.ClosedByPeer,
            ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally => SteamConnectionState.ProblemDetectedLocally,
            _ => SteamConnectionState.None,
        };
    }
}
