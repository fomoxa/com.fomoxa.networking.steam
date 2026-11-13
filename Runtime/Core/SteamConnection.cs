using System;
using System.Buffers.Binary;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Networking.Steam
{
    internal sealed class SteamConnection : ITransport, IConnectionData
    {
        public const int MostMessageBytes = 512 * 1024;

        private readonly SteamConnections owner;
        private readonly uint connection;
        private readonly byte[] connectionData = new byte[sizeof(ulong)];
        private byte[] pending;
        private bool closedByPeer;
        private bool released;

        public SteamConnection(SteamConnections owner, uint connection, ulong remoteSteamId)
        {
            this.owner = owner;
            this.connection = connection;
            RemoteSteamId = remoteSteamId;
            BinaryPrimitives.WriteUInt64LittleEndian(connectionData, remoteSteamId);
        }

        public ulong RemoteSteamId { get; }

        public TransportKind Kind => TransportKind.Message;

        public ReadOnlyMemory<byte> ConnectionData => connectionData;

        public SendOutcome Send(ReadOnlySpan<byte> bytes)
        {
            if (released)
            {
                return SendOutcome.Closed;
            }

            if (bytes.Length > MostMessageBytes)
            {
                return SendOutcome.TooLarge;
            }

            owner.Pump();
            if (closedByPeer)
            {
                return SendOutcome.Closed;
            }

            switch (owner.Sockets.SendUnreliable(connection, bytes))
            {
                case SteamSendResult.Sent:
                    return SendOutcome.Ok;
                case SteamSendResult.LimitExceeded:
                    return SendOutcome.WouldBlock;
                case SteamSendResult.NoConnection:
                    closedByPeer = true;
                    return SendOutcome.Closed;
                default:
                    return SendOutcome.Error;
            }
        }

        public ReceiveOutcome Receive(Span<byte> buffer)
        {
            if (released)
            {
                return ReceiveOutcome.Closed;
            }

            owner.Pump();
            if (pending == null)
            {
                pending = owner.Sockets.Receive(connection);
            }

            if (pending == null)
            {
                return closedByPeer ? ReceiveOutcome.Closed : ReceiveOutcome.WouldBlock;
            }

            if (pending.Length > buffer.Length)
            {
                return ReceiveOutcome.NeedCapacity(pending.Length);
            }

            int length = pending.Length;
            pending.CopyTo(buffer);
            pending = null;
            return ReceiveOutcome.Ok(length);
        }

        public void Change(SteamConnectionChange change)
        {
            if (change.IsClosed)
            {
                closedByPeer = true;
            }
        }

        public void CloseGracefully()
        {
            Release(true);
        }

        public void Dispose()
        {
            Release(false);
        }

        private void Release(bool linger)
        {
            if (released)
            {
                return;
            }

            released = true;
            pending = null;
            owner.Release(connection, linger);
        }
    }
}
