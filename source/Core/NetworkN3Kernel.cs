#if ZONDERQ_NETWORK_N3_PROBE
using System;
namespace ZonderqOS
{
    /// <summary>Dedicated CI boot profile for N3; never selected by normal builds.</summary>
    public sealed class NetworkN3Kernel : Kernel
    {
        protected override void BeforeRun()
        {
            base.BeforeRun();
            Console.WriteLine("[NETWORK-N3] starting ARP request/reply/cache probe");
            ArpStageProbe.Run();
        }
    }
}
#endif
