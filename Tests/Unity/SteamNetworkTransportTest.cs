using System;
using Fomoxa.Networking.Steam;
using Fomoxa.Networking.Transports;
using NUnit.Framework;
using UnityEngine;

namespace Fomoxa.Unity.Steam.Tests
{
    public sealed class SteamNetworkTransportTest
    {
        private GameObject owner;

        [SetUp]
        public void CreateOwner()
        {
            owner = new GameObject(nameof(SteamNetworkTransportTest));
        }

        [TearDown]
        public void DestroyOwner()
        {
            UnityEngine.Object.DestroyImmediate(owner);
        }

        [Test]
        public void CreatesASteamFactoryWithTheUdpFrameBudget()
        {
            ITransportFactory factory = owner.AddComponent<SteamNetworkTransport>().CreateFactory();

            Assert.That(factory, Is.TypeOf<SteamTransportFactory>());
            Assert.That(factory.FrameBudget, Is.EqualTo(1200));
        }

        [Test]
        public void SteamIsNotInitializedWithoutSteamApiInit()
        {
            Assert.That(new SteamworksSockets().IsInitialized, Is.False);
        }

        [Test]
        public void ListeningWithoutSteamTellsTheGameToInitializeSteam()
        {
            ITransportFactory factory = owner.AddComponent<SteamNetworkTransport>().CreateFactory();

            var error = Assert.Throws<InvalidOperationException>(() => factory.CreateListener(7777, out _));
            Assert.That(error.Message, Does.Contain("SteamAPI.Init()"));
        }

        [Test]
        public void ConnectingWithoutSteamTellsTheGameToInitializeSteam()
        {
            ITransportFactory factory = owner.AddComponent<SteamNetworkTransport>().CreateFactory();

            var error = Assert.Throws<InvalidOperationException>(() => factory.CreateConnector("76561197960265729", 7777));
            Assert.That(error.Message, Does.Contain("SteamAPI.Init()"));
        }
    }
}
