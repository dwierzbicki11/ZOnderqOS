using System.Collections.Generic;

namespace ZonderqOS
{
    /// <summary>
    /// Stable application-facing network device snapshot. UI code depends on
    /// this type instead of Cosmos NIC implementation details.
    /// </summary>
    public sealed class NetworkDeviceInfo
    {
        public string Name { get; internal set; } = "Unavailable";
        public string MacAddress { get; internal set; } = "--";
        public bool LinkUp { get; internal set; }
        public bool Ready { get; internal set; }
        public string IpAddress { get; internal set; } = "0.0.0.0";
        public string SubnetMask { get; internal set; } = "0.0.0.0";
        public string DefaultGateway { get; internal set; } = "0.0.0.0";
    }

    /// <summary>
    /// ZonderqOS network facade. Networking is disabled at build-profile level
    /// until the required Cosmos Gen3 hardware paths are stable on both targets.
    /// Consumers can still query deterministic state without touching Cosmos
    /// networking namespaces or architecture-specific adapters.
    /// </summary>
    public static class Network
    {
        private static readonly List<NetworkDeviceInfo> devices = new List<NetworkDeviceInfo>();

        public static IReadOnlyList<NetworkDeviceInfo> Devices => devices;
        public static NetworkDeviceInfo ActiveDevice => null;
        public static bool IsReady => false;
        public static string CurrentAddress => "0.0.0.0";

        public static void Initialize()
        {
            devices.Clear();
        }

        public static bool SetActiveDevice(int index) => false;
        public static bool ApplySavedConfiguration() => false;
        public static bool ConfigureDhcp() => false;
        public static bool ConfigureStatic(string ip, string subnet, string gateway, string dns) => false;
        public static string ResolveDns(string domain) => null;

        public static void SendUdpTest(string targetIp, int port, string text)
        {
            WriteMessage.WriteError("Networking is not supported in this ZonderqOS build.", "NET");
        }

        public static void ShowInterfaceInfo()
        {
            WriteMessage.WriteInfo("Networking is disabled in this ZonderqOS build.", "NET");
        }
    }
}

namespace Cosmos.Kernel.System.Network.Config
{
    /// <summary>
    /// Temporary bridge for legacy Settings UI. Keep the compatibility type
    /// read-only and route it through the ZonderqOS facade instead of Cosmos NIC
    /// internals. Remove this namespace shim when Settings no longer imports it.
    /// </summary>
    public static class NetworkConfigManager
    {
        public static string CurrentAddress => global::ZonderqOS.Network.CurrentAddress;
    }
}
