using System;

namespace Fomoxa.Networking.Steam
{
    public static class SteamPackage
    {
        public const string CoreVersion = "0.5.0";

        internal static void CheckCore(string projectCore)
        {
            if (projectCore != CoreVersion)
            {
                throw new InvalidOperationException($"com.fomoxa.networking.steam needs com.fomoxa.networking {CoreVersion}; the project has {projectCore}");
            }
        }
    }
}
