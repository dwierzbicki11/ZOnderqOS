using System.Collections.Generic;

namespace ZonderqOS
{
    /// <summary>
    /// Compatibility snapshot retained so non-network UI code can compile while
    /// networking is intentionally disabled in ZonderqOS.
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
    /// Networking is disabled until the Cosmos Gen3 hardware paths used by
    /// ZonderqOS are complete. No DHCP, DNS, ICMP, UDP or NIC initialization
    /// is performed from this facade.
    /// </summary>
    public static class Network
    {
        public static List<NetworkDeviceInfo> Devices { get; } = new List<NetworkDeviceInfo>();
        public static NetworkDeviceInfo ActiveDevice => null;
        public static bool IsReady => false;
        public static string CurrentAddress => "0.0.0.0";

        public static void Initialize()
        {
            Devices.Clear();
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
    /// Legacy read-only bridge for SettingsApp while networking is removed.
    /// </summary>
    public static class NetworkConfigManager
    {
        public static string CurrentAddress => null;
    }
}
