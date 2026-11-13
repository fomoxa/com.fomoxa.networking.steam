using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Steam;
using Fomoxa.Networking.Transports;
using NUnit.Framework;
using Steamworks;

namespace Fomoxa.Unity.Steam.Tests
{
    public sealed class SteamworksSocketsTest
    {
        private const int VirtualPort = 7777;
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

        private bool steamStarted;
        private SteamworksSockets sockets;
        private ulong localSteamId;
        private uint first;
        private uint second;

        [OneTimeSetUp]
        public void StartSteam()
        {
            ESteamAPIInitResult result = SteamAPI.InitEx(out string error);
            if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                Assert.Ignore($"Steam is not available ({result}: {error}); open Steam, sign in and run Tools/steam-check.sh");
            }

            steamStarted = true;
            localSteamId = SteamUser.GetSteamID().m_SteamID;
        }

        [OneTimeTearDown]
        public void StopSteam()
        {
            if (steamStarted)
            {
                SteamAPI.Shutdown();
            }
        }

        [SetUp]
        public void CreateSockets()
        {
            sockets = new SteamworksSockets();
            first = 0;
            second = 0;
        }

        [TearDown]
        public void CloseSockets()
        {
            if (first != 0)
            {
                sockets.Close(first, false);
            }

            if (second != 0)
            {
                sockets.Close(second, false);
            }

            sockets.Unwatch();
        }

        [Test]
        public void SteamIsInitializedAfterSteamApiInit()
        {
            Assert.That(sockets.IsInitialized, Is.True);
        }

        [Test]
        public void OpensAndClosesAListenSocket()
        {
            uint listenSocket = sockets.CreateListenSocket(VirtualPort);

            Assert.That(listenSocket, Is.Not.Zero);
            sockets.CloseListenSocket(listenSocket);
        }

        [Test]
        public void SteamRefusesAConnectionToTheLocalSteamId()
        {
            Assert.That(sockets.Connect(localSteamId, VirtualPort), Is.Zero);
        }

        [Test]
        public void AConnectorToTheLocalSteamIdFails()
        {
            var factory = new SteamTransportFactory(sockets);
            ITransportConnector connector = factory.CreateConnector(localSteamId.ToString(CultureInfo.InvariantCulture), VirtualPort);

            Assert.That(connector.Poll(out ITransport transport), Is.EqualTo(ConnectStatus.Failed));
            Assert.That(transport, Is.Null);
            Assert.That(factory.Connections.OpenHandles, Is.Zero);
        }

        [Test]
        public void ReceivesNothingBeforeTheOtherEndSends()
        {
            OpenPair(useNetworkLoopback: false);

            Assert.That(sockets.Receive(second), Is.Null);
        }

        [Test]
        public void MessagesArriveWholeAndInOrder()
        {
            OpenPair(useNetworkLoopback: false);
            byte[] small = { 1, 2, 3 };
            byte[] large = Enumerable.Range(0, 4000).Select(index => (byte)index).ToArray();

            Assert.That(sockets.SendUnreliable(first, small), Is.EqualTo(SteamSendResult.Sent));
            Assert.That(sockets.SendUnreliable(first, large), Is.EqualTo(SteamSendResult.Sent));

            byte[] firstReceived = null;
            WaitFor(() => (firstReceived = sockets.Receive(second)) != null);
            byte[] secondReceived = null;
            WaitFor(() => (secondReceived = sockets.Receive(second)) != null);
            Assert.That(firstReceived, Is.EqualTo(small));
            Assert.That(secondReceived, Is.EqualTo(large));
        }

        [Test]
        public void AFullSendBufferIsLimitExceeded()
        {
            OpenPair(useNetworkLoopback: true);
            var message = new byte[64 * 1024];

            SteamSendResult result = SteamSendResult.Sent;
            for (int sent = 0; sent < 256 && result == SteamSendResult.Sent; sent++)
            {
                result = sockets.SendUnreliable(first, message);
            }

            Assert.That(result, Is.EqualTo(SteamSendResult.LimitExceeded));
        }

        [Test]
        public void ClosingOneEndReachesTheOtherAsAChangeWithTheRemoteSteamId()
        {
            OpenPair(useNetworkLoopback: false);
            sockets.Watch();

            sockets.Close(first, false);
            uint closed = first;
            first = 0;

            SteamConnectionChange change = WaitForChange(second);
            Assert.That(change.IsClosed, Is.True);
            Assert.That(change.ListenSocket, Is.Zero);
            Assert.That(change.RemoteSteamId, Is.EqualTo(localSteamId));
            Assert.That(change.Connection, Is.Not.EqualTo(closed));
        }

        [Test]
        public void SendingAfterTheOtherEndClosedIsNoConnection()
        {
            OpenPair(useNetworkLoopback: false);
            sockets.Watch();

            sockets.Close(first, false);
            first = 0;
            WaitForChange(second);

            Assert.That(sockets.SendUnreliable(second, new byte[] { 1 }), Is.EqualTo(SteamSendResult.NoConnection));
        }

        [Test]
        public void NoChangeIsQueuedAfterUnwatch()
        {
            OpenPair(useNetworkLoopback: false);
            sockets.Watch();
            sockets.Unwatch();

            sockets.Close(first, false);
            first = 0;
            RunCallbacksFor(TimeSpan.FromMilliseconds(500));

            Assert.That(sockets.TryTakeChange(out _), Is.False);
        }

        private void OpenPair(bool useNetworkLoopback)
        {
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID64(localSteamId);
            var otherIdentity = identity;
            Assert.That(
                SteamNetworkingSockets.CreateSocketPair(out HSteamNetConnection one, out HSteamNetConnection two, useNetworkLoopback, ref identity, ref otherIdentity),
                Is.True);
            first = one.m_HSteamNetConnection;
            second = two.m_HSteamNetConnection;
        }

        private SteamConnectionChange WaitForChange(uint connection)
        {
            SteamConnectionChange found = default;
            WaitFor(() =>
            {
                while (sockets.TryTakeChange(out SteamConnectionChange change))
                {
                    if (change.Connection == connection)
                    {
                        found = change;
                        return true;
                    }
                }

                return false;
            });
            return found;
        }

        private static void WaitFor(Func<bool> condition)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (true)
            {
                SteamAPI.RunCallbacks();
                if (condition())
                {
                    return;
                }

                if (clock.Elapsed > Patience)
                {
                    Assert.Fail($"nothing happened within {Patience.TotalSeconds} s");
                }

                Thread.Sleep(10);
            }
        }

        private static void RunCallbacksFor(TimeSpan duration)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (clock.Elapsed < duration)
            {
                SteamAPI.RunCallbacks();
                Thread.Sleep(10);
            }
        }
    }
}
