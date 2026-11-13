using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Networking.Steam
{
    internal sealed class SteamConnector : ITransportConnector
    {
        private readonly SteamConnections owner;
        private readonly uint connection;
        private readonly ulong remoteSteamId;
        private SteamConnectionState state;
        private bool finished;

        public SteamConnector(SteamConnections owner, uint connection, ulong remoteSteamId)
        {
            this.owner = owner;
            this.connection = connection;
            this.remoteSteamId = remoteSteamId;
            state = SteamConnectionState.Connecting;
        }

        public static SteamConnector Unreachable(SteamConnections owner) =>
            new SteamConnector(owner, 0, 0) { state = SteamConnectionState.ProblemDetectedLocally };

        public ConnectStatus Poll(out ITransport transport)
        {
            transport = null;
            if (finished)
            {
                return ConnectStatus.Failed;
            }

            if (connection != 0)
            {
                owner.Pump();
            }

            if (state == SteamConnectionState.Connected)
            {
                finished = true;
                transport = owner.Adopt(connection, remoteSteamId);
                return ConnectStatus.Connected;
            }

            if (state == SteamConnectionState.ClosedByPeer || state == SteamConnectionState.ProblemDetectedLocally)
            {
                Finish();
                return ConnectStatus.Failed;
            }

            return ConnectStatus.Pending;
        }

        public void Change(SteamConnectionChange change)
        {
            state = change.State;
        }

        public void Dispose()
        {
            Finish();
        }

        private void Finish()
        {
            if (finished)
            {
                return;
            }

            finished = true;
            if (connection != 0)
            {
                owner.Release(connection, false);
            }
        }
    }
}
