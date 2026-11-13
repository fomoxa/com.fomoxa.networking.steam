using System;
using System.Buffers.Binary;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Steam.Tests
{
    public sealed class SteamTransportTest
    {
        private const ulong HostId = 76561198000000001;
        private const ulong ClientId = 76561198000000002;
        private const ushort VirtualPort = 7;

        private FakeSteamNetwork network;
        private FakeSteamSockets hostSockets;
        private FakeSteamSockets clientSockets;
        private SteamTransportFactory host;
        private SteamTransportFactory client;

        [SetUp]
        public void SetUp()
        {
            network = new FakeSteamNetwork();
            hostSockets = network.Join(HostId);
            clientSockets = network.Join(ClientId);
            host = new SteamTransportFactory(hostSockets);
            client = new SteamTransportFactory(clientSockets);
        }

        [Test]
        public void TheFrameBudgetStaysUnderTheSteamMtu()
        {
            Assert.AreEqual(1200, host.FrameBudget);
        }

        [Test]
        public void ListeningBeforeSteamIsInitializedThrows()
        {
            hostSockets.IsInitialized = false;

            var thrown = Assert.Throws<InvalidOperationException>(() => host.CreateListener(VirtualPort, out _));

            StringAssert.Contains("SteamAPI.Init()", thrown.Message);
            Assert.AreEqual(0, hostSockets.WatchCalls);
        }

        [Test]
        public void ConnectingBeforeSteamIsInitializedThrows()
        {
            clientSockets.IsInitialized = false;

            var thrown = Assert.Throws<InvalidOperationException>(() => client.CreateConnector(HostId.ToString(), VirtualPort));

            StringAssert.Contains("SteamAPI.RunCallbacks()", thrown.Message);
        }

        [Test]
        public void TheListenerKeepsTheVirtualPort()
        {
            using IListenerTransport listener = host.CreateListener(VirtualPort, out ushort boundPort);

            Assert.AreEqual(VirtualPort, boundPort);
        }

        [Test]
        public void AClientConnectsAndTheHostSeesItsSteamId()
        {
            using IListenerTransport listener = host.CreateListener(VirtualPort, out _);
            using ITransportConnector connector = client.CreateConnector(HostId.ToString(), VirtualPort);

            Assert.AreEqual(ConnectStatus.Pending, connector.Poll(out _));
            AcceptOutcome accepted = listener.Accept();
            Assert.AreEqual(ConnectStatus.Connected, connector.Poll(out ITransport clientEnd));

            Assert.AreEqual(AcceptStatus.Accepted, accepted.Status);
            Assert.AreEqual(TransportKind.Message, accepted.Transport.Kind);
            Assert.AreEqual(TransportKind.Message, clientEnd.Kind);
            Assert.AreEqual(ClientId, BinaryPrimitives.ReadUInt64LittleEndian(((IConnectionData)accepted.Transport).ConnectionData.Span));
            Assert.AreEqual(HostId, BinaryPrimitives.ReadUInt64LittleEndian(((IConnectionData)clientEnd).ConnectionData.Span));
            Assert.AreEqual(ConnectStatus.Failed, connector.Poll(out _));
        }

        [Test]
        public void MessagesKeepTheirBoundariesBothWays()
        {
            (ITransport hostEnd, ITransport clientEnd) = Connect();

            Assert.AreEqual(TransportSignal.Ok, clientEnd.Send(new byte[] { 1, 2, 3 }).Signal);
            Assert.AreEqual(TransportSignal.Ok, clientEnd.Send(new byte[] { 4 }).Signal);
            Assert.AreEqual(TransportSignal.Ok, hostEnd.Send(new byte[] { 9, 8 }).Signal);

            var buffer = new byte[16];
            ReceiveOutcome first = hostEnd.Receive(buffer);
            Assert.AreEqual(TransportSignal.Ok, first.Signal);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, buffer[..first.Count]);
            ReceiveOutcome second = hostEnd.Receive(buffer);
            CollectionAssert.AreEqual(new byte[] { 4 }, buffer[..second.Count]);
            Assert.AreEqual(TransportSignal.WouldBlock, hostEnd.Receive(buffer).Signal);
            ReceiveOutcome reply = clientEnd.Receive(buffer);
            CollectionAssert.AreEqual(new byte[] { 9, 8 }, buffer[..reply.Count]);
        }

        [Test]
        public void ASmallBufferAsksForCapacityAndKeepsTheMessage()
        {
            (ITransport hostEnd, ITransport clientEnd) = Connect();
            clientEnd.Send(new byte[] { 1, 2, 3, 4 });

            ReceiveOutcome tooSmall = hostEnd.Receive(new byte[2]);
            var buffer = new byte[4];
            ReceiveOutcome received = hostEnd.Receive(buffer);

            Assert.AreEqual(TransportSignal.NeedCapacity, tooSmall.Signal);
            Assert.AreEqual(4, tooSmall.Count);
            Assert.AreEqual(TransportSignal.Ok, received.Signal);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, buffer);
        }

        [Test]
        public void AFullSteamSendQueueIsWouldBlock()
        {
            (_, ITransport clientEnd) = Connect();
            clientSockets.QueuedMessageLimit = 1;

            Assert.AreEqual(TransportSignal.Ok, clientEnd.Send(new byte[] { 1 }).Signal);
            Assert.AreEqual(TransportSignal.WouldBlock, clientEnd.Send(new byte[] { 2 }).Signal);
        }

        [Test]
        public void AMessageOverTheSteamLimitIsTooLarge()
        {
            (_, ITransport clientEnd) = Connect();

            Assert.AreEqual(TransportSignal.TooLarge, clientEnd.Send(new byte[SteamConnection.MostMessageBytes + 1]).Signal);
            Assert.AreEqual(TransportSignal.Ok, clientEnd.Send(new byte[SteamConnection.MostMessageBytes]).Signal);
        }

        [Test]
        public void AfterThePeerClosesTheQueuedMessagesArriveThenTheConnectionIsClosed()
        {
            (ITransport hostEnd, ITransport clientEnd) = Connect();
            clientEnd.Send(new byte[] { 7 });

            clientEnd.CloseGracefully();

            var buffer = new byte[4];
            Assert.AreEqual(TransportSignal.Ok, hostEnd.Receive(buffer).Signal);
            Assert.AreEqual(TransportSignal.Closed, hostEnd.Receive(buffer).Signal);
            Assert.AreEqual(TransportSignal.Closed, hostEnd.Send(new byte[] { 1 }).Signal);
            Assert.AreEqual(TransportSignal.Closed, clientEnd.Send(new byte[] { 1 }).Signal);
            Assert.AreEqual(TransportSignal.Closed, clientEnd.Receive(buffer).Signal);
            CollectionAssert.AreEqual(new[] { true }, clientSockets.Lingers);
        }

        [Test]
        public void DisposingAConnectionClosesItWithoutLinger()
        {
            (_, ITransport clientEnd) = Connect();

            clientEnd.Dispose();
            clientEnd.Dispose();

            CollectionAssert.AreEqual(new[] { false }, clientSockets.Lingers);
        }

        [Test]
        public void ConnectingToASteamIdWithoutAListenerFails()
        {
            using ITransportConnector connector = client.CreateConnector(HostId.ToString(), VirtualPort);

            Assert.AreEqual(ConnectStatus.Failed, connector.Poll(out ITransport transport));

            Assert.IsNull(transport);
            Assert.AreEqual(1, clientSockets.Closed.Count);
            Assert.AreEqual(0, client.Connections.OpenHandles);
            Assert.AreEqual(0, clientSockets.Watching);
        }

        [TestCase("")]
        [TestCase("0")]
        [TestCase("127.0.0.1")]
        [TestCase("-5")]
        [TestCase(" 76561198000000001")]
        public void AnAddressThatIsNotASteamIdFailsWithoutOpeningAConnection(string address)
        {
            using ITransportConnector connector = client.CreateConnector(address, VirtualPort);

            Assert.AreEqual(ConnectStatus.Failed, connector.Poll(out _));
            Assert.AreEqual(0, clientSockets.WatchCalls);
            Assert.IsEmpty(clientSockets.Closed);
        }

        [Test]
        public void DisposingAConnectorBeforeItConnectsClosesTheSteamConnection()
        {
            host.CreateListener(VirtualPort, out _);
            ITransportConnector connector = client.CreateConnector(HostId.ToString(), VirtualPort);

            connector.Dispose();

            Assert.AreEqual(1, clientSockets.Closed.Count);
            Assert.AreEqual(0, client.Connections.OpenHandles);
        }

        [Test]
        public void AnArrivalThatClosesBeforeAcceptIsDropped()
        {
            using IListenerTransport listener = host.CreateListener(VirtualPort, out _);
            ITransportConnector connector = client.CreateConnector(HostId.ToString(), VirtualPort);
            hostSockets.RefuseAccept = true;

            Assert.AreEqual(AcceptStatus.Pending, listener.Accept().Status);

            Assert.AreEqual(1, hostSockets.Closed.Count);
            Assert.AreEqual(ConnectStatus.Failed, connector.Poll(out _));
            Assert.AreEqual(1, host.Connections.OpenHandles);
        }

        [Test]
        public void AConnectionThatClosesWhileWaitingForAcceptIsNotHandedOut()
        {
            using IListenerTransport listener = host.CreateListener(VirtualPort, out _);
            ITransportConnector connector = client.CreateConnector(HostId.ToString(), VirtualPort);
            host.Connections.Pump();

            connector.Dispose();

            Assert.AreEqual(AcceptStatus.Pending, listener.Accept().Status);
            Assert.AreEqual(1, hostSockets.Closed.Count);
            Assert.AreEqual(1, host.Connections.OpenHandles);
        }

        [Test]
        public void SteamIsWatchedWhileAHandleIsOpen()
        {
            IListenerTransport listener = host.CreateListener(VirtualPort, out _);
            ITransportConnector connector = client.CreateConnector(HostId.ToString(), VirtualPort);
            AcceptOutcome accepted = listener.Accept();
            connector.Poll(out ITransport clientEnd);
            Assert.AreEqual(1, hostSockets.Watching);
            Assert.AreEqual(2, host.Connections.OpenHandles);

            listener.Dispose();
            Assert.AreEqual(1, hostSockets.Watching);
            accepted.Transport.Dispose();
            clientEnd.Dispose();

            Assert.AreEqual(0, hostSockets.Watching);
            Assert.AreEqual(1, hostSockets.WatchCalls);
            Assert.AreEqual(0, clientSockets.Watching);
            CollectionAssert.AreEqual(new[] { 100u }, hostSockets.ClosedListenSockets);
        }

        [Test]
        public void ChangesOfConnectionsThisFactoryDoesNotOwnAreIgnored()
        {
            using IListenerTransport listener = host.CreateListener(VirtualPort, out _);

            hostSockets.Raise(new SteamConnectionChange(900, 0, SteamConnectionState.Connected, ClientId));
            hostSockets.Raise(new SteamConnectionChange(901, 999, SteamConnectionState.Connecting, ClientId));

            Assert.AreEqual(AcceptStatus.Pending, listener.Accept().Status);
            Assert.IsEmpty(hostSockets.Closed);
            Assert.AreEqual(1, host.Connections.OpenHandles);
        }

        [Test]
        public void AcceptAfterDisposeIsAnError()
        {
            IListenerTransport listener = host.CreateListener(VirtualPort, out _);

            listener.Dispose();
            listener.Dispose();

            Assert.AreEqual(AcceptStatus.Error, listener.Accept().Status);
            Assert.AreEqual(1, hostSockets.ClosedListenSockets.Count);
        }

        private (ITransport Host, ITransport Client) Connect()
        {
            IListenerTransport listener = host.CreateListener(VirtualPort, out _);
            ITransportConnector connector = client.CreateConnector(HostId.ToString(), VirtualPort);
            AcceptOutcome accepted = listener.Accept();
            Assert.AreEqual(ConnectStatus.Connected, connector.Poll(out ITransport clientEnd));
            return (accepted.Transport, clientEnd);
        }
    }
}
