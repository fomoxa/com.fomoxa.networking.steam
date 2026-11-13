using System.Collections.Generic;
using Fomoxa.Net.Transports;

namespace Fomoxa.Networking.Steam
{
    internal sealed class SteamListener : IListenerTransport
    {
        private readonly SteamConnections owner;
        private readonly uint listenSocket;
        private readonly Dictionary<uint, ulong> arriving = new Dictionary<uint, ulong>();
        private readonly List<SteamConnectionChange> ready = new List<SteamConnectionChange>();
        private bool disposed;

        public SteamListener(SteamConnections owner, uint listenSocket)
        {
            this.owner = owner;
            this.listenSocket = listenSocket;
        }

        public AcceptOutcome Accept()
        {
            if (disposed)
            {
                return AcceptOutcome.Error;
            }

            owner.Pump();
            if (ready.Count == 0)
            {
                return AcceptOutcome.Pending;
            }

            SteamConnectionChange arrival = ready[0];
            ready.RemoveAt(0);
            return AcceptOutcome.Accepted(owner.Adopt(arrival.Connection, arrival.RemoteSteamId));
        }

        public void Change(SteamConnectionChange change)
        {
            if (change.State == SteamConnectionState.Connecting)
            {
                if (!arriving.ContainsKey(change.Connection) && IndexOfReady(change.Connection) < 0 && owner.AcceptIncoming(change.Connection))
                {
                    arriving.Add(change.Connection, change.RemoteSteamId);
                }
            }
            else if (change.State == SteamConnectionState.Connected)
            {
                if (arriving.TryGetValue(change.Connection, out ulong remoteSteamId))
                {
                    arriving.Remove(change.Connection);
                    ready.Add(new SteamConnectionChange(change.Connection, listenSocket, change.State, remoteSteamId));
                }
            }
            else if (change.IsClosed)
            {
                int readyIndex = IndexOfReady(change.Connection);
                if (arriving.Remove(change.Connection))
                {
                    owner.Release(change.Connection, false);
                }
                else if (readyIndex >= 0)
                {
                    ready.RemoveAt(readyIndex);
                    owner.Release(change.Connection, false);
                }
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreach (uint connection in new List<uint>(arriving.Keys))
            {
                owner.Release(connection, false);
            }

            foreach (SteamConnectionChange arrival in ready)
            {
                owner.Release(arrival.Connection, false);
            }

            arriving.Clear();
            ready.Clear();
            owner.ReleaseListener(listenSocket);
        }

        private int IndexOfReady(uint connection)
        {
            for (int index = 0; index < ready.Count; index++)
            {
                if (ready[index].Connection == connection)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
