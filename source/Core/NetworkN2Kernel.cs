#if ZONDERQ_NETWORK_N2_PROBE
using Cosmos.Kernel.Core.IO;

namespace ZonderqOS
{
    /// <summary>Dedicated CI boot profile for N2; never selected by normal builds.</summary>
    public sealed class NetworkN2Kernel : Kernel
    {
        protected override void BeforeRun()
        {
            base.BeforeRun();
            Serial.WriteString("[NETWORK-N2] starting raw Ethernet probe\n");
            EthernetStageProbe.RunTx();
        }
    }
}
#endif
