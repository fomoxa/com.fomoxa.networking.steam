using Fomoxa.Networking.Steam;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Unity.Steam
{
    public sealed class SteamNetworkTransport : NetworkTransport
    {
        public override ITransportFactory CreateFactory() => new SteamTransportFactory(new SteamworksSockets());
    }
}
