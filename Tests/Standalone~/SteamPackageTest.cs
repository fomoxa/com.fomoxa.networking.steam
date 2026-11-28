using System;
using NUnit.Framework;

namespace Fomoxa.Networking.Steam.Tests
{
    public sealed class SteamPackageTest
    {
        [Test]
        public void CoreVersionIsTheVersionOfTheCoreInTheRepository()
        {
            Assert.That(SteamPackage.CoreVersion, Is.EqualTo(NetworkRuntime.Version));
        }

        [Test]
        public void AFactoryForTheCoreOfTheProjectIsCreated()
        {
            Assert.DoesNotThrow(() => new SteamTransportFactory(new FakeSteamNetwork().Join(1)));
        }

        [Test]
        public void AFactoryForAnotherCoreVersionThrows()
        {
            Assert.Throws<InvalidOperationException>(() => new SteamTransportFactory(new FakeSteamNetwork().Join(1), "0.3.6"));
        }

        [Test]
        public void AnotherCoreVersionThrowsWithBothVersions()
        {
            var thrown = Assert.Throws<InvalidOperationException>(() => SteamPackage.CheckCore("0.3.3"));

            Assert.That(thrown.Message, Is.EqualTo("com.fomoxa.networking.steam needs com.fomoxa.networking 0.6.0; the project has 0.3.3"));
        }
    }
}
