using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using BundleFixture;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Standalone;
using NUnit.Framework;

namespace Fomoxa.Networking.Steam.Tests
{
    public sealed class SteamRuntimeTest
    {
        private const ulong HostId = 76561198000000001;
        private const ulong ClientId = 76561198000000002;
        private const ushort VirtualPort = 0;
        private const uint PlayerPrefab = 0x0000_0701;
        private const uint Fingerprint = 0x0000_F1D0;
        private const double FrameSeconds = 1.0 / 30;

        private FomoxaRegistry registry;
        private TimeSpan now;
        private List<Exception> exceptions;
        private List<string> errors;
        private NetworkLog log;
        private FakeSteamNetwork network;
        private NetworkRuntime server;
        private NetworkRuntime client;
        private List<ulong> seenSteamIds;
        private List<StopReason> serverStops;

        [SetUp]
        public void SetUp()
        {
            registry = TestObjects.Registry();
            now = TimeSpan.Zero;
            exceptions = new List<Exception>();
            errors = new List<string>();
            log = new NetworkLog(exceptions.Add, message => { }, errors.Add);
            network = new FakeSteamNetwork();
            seenSteamIds = new List<ulong>();
            serverStops = new List<StopReason>();
            server = Create(new SteamTransportFactory(network.Join(HostId)));
            server.ServerManager.Authenticator = new SteamIdAuthenticator(seenSteamIds);
            server.ServerManager.OnRemoteConnectionState += args =>
            {
                if (args.State == ConnectionState.Started)
                {
                    server.ServerManager.Spawn(new StandaloneEntity(PlayerPrefab, Array.Empty<EntityBehaviour>()), args.PeerId);
                }
                else if (args.State == ConnectionState.Stopped)
                {
                    serverStops.Add(args.Reason);
                }
            };
            client = Create(new SteamTransportFactory(network.Join(ClientId)));
        }

        [TearDown]
        public void TearDown()
        {
            client.ClientManager.StopConnection();
            server.ServerManager.StopConnection();
            Assert.IsEmpty(exceptions);
            Assert.IsEmpty(errors);
        }

        [Test]
        public void AClientStartsOverSteamAndTheAuthenticatorSeesItsSteamId()
        {
            server.ServerManager.StartConnection(VirtualPort);
            client.ClientManager.StartConnection(HostId.ToString(), VirtualPort);

            Run(30);

            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            Assert.AreEqual(1, client.ClientManager.Spawned.Count);
            CollectionAssert.AreEqual(new[] { ClientId }, seenSteamIds);
        }

        [Test]
        public void TheServerSeesTheClientLeaveWhenItStops()
        {
            server.ServerManager.StartConnection(VirtualPort);
            client.ClientManager.StartConnection(HostId.ToString(), VirtualPort);
            Run(30);

            client.ClientManager.StopConnection();
            Run(30);

            Assert.AreEqual(1, serverStops.Count);
            Assert.AreEqual(0, server.ServerManager.PeerCount);
        }

        private NetworkRuntime Create(SteamTransportFactory factory)
        {
            var prefabs = new StandalonePrefabs();
            prefabs.Register(PlayerPrefab, Fingerprint, () => new StandaloneEntity(PlayerPrefab, Array.Empty<EntityBehaviour>()));
            return StandaloneRuntime.Create(registry, new NetworkSettings(), factory, prefabs, new StandaloneBehaviours(), new NoSceneFiles(), () => now, log);
        }

        private void Run(int frames)
        {
            for (int index = 0; index < frames; index++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                server.BeginFrame(now, FrameSeconds);
                client.BeginFrame(now, FrameSeconds);
                server.EndFrame();
                client.EndFrame();
            }
        }

        private sealed class SteamIdAuthenticator : IServerAuthenticator
        {
            private readonly List<ulong> seen;

            public SteamIdAuthenticator(List<ulong> seen)
            {
                this.seen = seen;
            }

            public void RegisterServer(ServerAuthentication authentication)
            {
            }

            public void OnServerAuthenticating(ServerAuthenticationContext context)
            {
                seen.Add(BinaryPrimitives.ReadUInt64LittleEndian(context.ConnectionData.Span));
                context.Accept();
            }
        }

        private sealed class NoSceneFiles : ISceneFiles
        {
            public bool Knows(uint sceneId) => false;

            public byte[] Read(uint sceneId) => throw new InvalidOperationException($"no scene file 0x{sceneId:X8}");
        }
    }
}
