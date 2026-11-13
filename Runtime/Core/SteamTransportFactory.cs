using System;
using System.Globalization;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Networking.Steam
{
    internal sealed class SteamTransportFactory : ITransportFactory
    {
        public const int Budget = 1200;

        private readonly SteamConnections connections;

        public SteamTransportFactory(ISteamSockets sockets)
        {
            connections = new SteamConnections(sockets);
        }

        public int FrameBudget => Budget;

        internal SteamConnections Connections => connections;

        public IListenerTransport CreateListener(ushort port, out ushort boundPort)
        {
            SteamListener listener = connections.Listen(port);
            boundPort = port;
            return listener;
        }

        public ITransportConnector CreateConnector(string address, ushort port)
        {
            connections.RequireSteam();
            return TryReadSteamId(address, out ulong steamId)
                ? connections.Connect(steamId, port)
                : SteamConnector.Unreachable(connections);
        }

        private static bool TryReadSteamId(string address, out ulong steamId) =>
            ulong.TryParse(address, NumberStyles.None, CultureInfo.InvariantCulture, out steamId) && steamId != 0;
    }
}
