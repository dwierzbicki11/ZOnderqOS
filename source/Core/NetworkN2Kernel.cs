#if ZONDERQ_NETWORK_N2_PROBE
using System;

namespace ZonderqOS
{
    /// <summary>Dedicated CI boot profile for N2; never selected by normal builds.</summary>
    public sealed class NetworkN2Kernel : Kernel
    {
        protected override void BeforeRun()
        {
            base.BeforeRun();
            Console.WriteLine("[NETWORK-N2] starting raw Ethernet TX/RX probe");
            EthernetStageProbe.Run();
        }
    }
}
#endif
