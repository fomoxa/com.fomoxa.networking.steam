using System;
using System.Collections.Generic;

namespace Fomoxa.Networking.Steam
{
    internal sealed class SteamConnections
    {
        private readonly Dictionary<uint, SteamListener> listeners = new Dictionary<uint, SteamListener>();
        private readonly Dictionary<uint, SteamConnector> connectors = new Dictionary<uint, SteamConnector>();
        private readonly Dictionary<uint, SteamConnection> connections = new Dictionary<uint, SteamConnection>();
        private int openHandles;

        public SteamConnections(ISteamSockets sockets)
        {
            Sockets = sockets ?? throw new ArgumentNullException(nameof(sockets));
        }

        public ISteamSockets Sockets { get; }

        public int OpenHandles => openHandles;

        public void RequireSteam()
        {
            if (!Sockets.IsInitialized)
            {
                throw new InvalidOperationException("Steam is not initialized: call SteamAPI.Init() and run SteamAPI.RunCallbacks() every frame before starting a Steam connection");
            }
        }

        public SteamListener Listen(int virtualPort)
        {
            RequireSteam();
            Opened();
            uint listenSocket = Sockets.CreateListenSocket(virtualPort);
            if (listenSocket == 0)
            {
                Released();
                throw new InvalidOperationException($"Steam did not open a listen socket on virtual port {virtualPort}");
            }

            var listener = new SteamListener(this, listenSocket);
            listeners.Add(listenSocket, listener);
            return listener;
        }

        public SteamConnector Connect(ulong steamId, int virtualPort)
        {
            RequireSteam();
            Opened();
            uint connection = Sockets.Connect(steamId, virtualPort);
            if (connection == 0)
            {
                Released();
                return SteamConnector.Unreachable(this);
            }

            var connector = new SteamConnector(this, connection, steamId);
            connectors.Add(connection, connector);
            return connector;
        }

        public void Pump()
        {
            while (Sockets.TryTakeChange(out SteamConnectionChange change))
            {
                if (connections.TryGetValue(change.Connection, out SteamConnection connection))
                {
                    connection.Change(change);
                }
                else if (connectors.TryGetValue(change.Connection, out SteamConnector connector))
                {
                    connector.Change(change);
                }
                else if (change.ListenSocket != 0 && listeners.TryGetValue(change.ListenSocket, out SteamListener listener))
                {
                    listener.Change(change);
                }
            }
        }

        public bool AcceptIncoming(uint connection)
        {
            if (Sockets.Accept(connection))
            {
                Opened();
                return true;
            }

            Sockets.Close(connection, false);
            return false;
        }

        public SteamConnection Adopt(uint connection, ulong remoteSteamId)
        {
            connectors.Remove(connection);
            var adopted = new SteamConnection(this, connection, remoteSteamId);
            connections.Add(connection, adopted);
            return adopted;
        }

        public void Release(uint connection, bool linger)
        {
            connectors.Remove(connection);
            connections.Remove(connection);
            Sockets.Close(connection, linger);
            Released();
        }

        public void ReleaseListener(uint listenSocket)
        {
            if (listeners.Remove(listenSocket))
            {
                Sockets.CloseListenSocket(listenSocket);
                Released();
            }
        }

        private void Opened()
        {
            if (openHandles++ == 0)
            {
                Sockets.Watch();
            }
        }

        private void Released()
        {
            if (--openHandles == 0)
            {
                Sockets.Unwatch();
            }
        }
    }
}
