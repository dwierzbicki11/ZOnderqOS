using System;
using System.Threading;
using Sys = Cosmos.Kernel.System;

namespace ZonderqOS
{
    /// <summary>
    /// Concrete N3-only kernel entrypoint. The project file includes this source only
    /// for CosmosNetworkStageN3 builds and removes the normal interactive Kernel.cs.
    /// This avoids relying on CosmosKernelClass selection, which the Gen3 image path
    /// currently does not honor for the produced boot image.
    /// </summary>
    public sealed class Kernel : Sys.Kernel
    {
        protected override void BeforeRun()
        {
            Console.WriteLine("[NETWORK-N3] starting ARP request/reply/cache probe");
            ArpStageProbe.Run();
        }

        protected override void Run()
        {
            // RX completion is interrupt/callback driven. Keep the probe alive without
            // entering storage, login, GUI or the interactive shell.
            Thread.Sleep(1000);
        }
    }
}
