namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Read-only compatibility snapshot for older GUI code. Cosmos Gen3 renamed
    /// IPConfig.IPAddress to Address and removed NetworkConfigManager; keeping
    /// this tiny projection avoids leaking kernel networking internals into UI code.
    /// </summary>
    public sealed class IPConfig
    {
        public global::Cosmos.Kernel.System.Network.Address IPAddress { get; }
        public global::Cosmos.Kernel.System.Network.Address SubnetMask { get; }
        public global::Cosmos.Kernel.System.Network.Address DefaultGateway { get; }

        internal IPConfig(
            global::Cosmos.Kernel.System.Network.Address ipAddress,
            global::Cosmos.Kernel.System.Network.Address subnetMask,
            global::Cosmos.Kernel.System.Network.Address defaultGateway)
        {
            IPAddress = ipAddress;
            SubnetMask = subnetMask;
            DefaultGateway = defaultGateway;
        }
    }
}
