#if ZONDERQ_REALTEK_READONLY_MMIO
using System.Threading;
using DiagnosticLog = Cosmos.Kernel.System.Diagnostics.Log;
using Sys = Cosmos.Kernel.System;
using ZonderqOS.Drivers;

namespace ZonderqOS
{
    /// <summary>
    /// Isolated serial-only hardware inspection kernel for ASUS RTL810xE.
    /// No VFS, GUI, accounts, network protocol stack or host disk writes.
    /// A real snapshot requires the exact PCI function and a mapped BAR2.
    /// </summary>
    public sealed class NetworkRealtekReadOnlyKernel : Sys.Kernel
    {
        private static int heartbeat;

        protected override void BeforeRun()
        {
            DiagnosticLog.WriteString("[NET3095-RTL810XE] BOOT read-only physical probe\n");
            if (Realtek810xEReadOnlyProbeDriver.ProbeOffers != 1 ||
                Realtek810xEReadOnlyProbeDriver.SuccessfulSamples != 1)
            {
                DiagnosticLog.WriteString("[NET3095-RTL810XE][FAIL] no unique valid MMIO snapshot; offers=" +
                    Realtek810xEReadOnlyProbeDriver.ProbeOffers +
                    " samples=" + Realtek810xEReadOnlyProbeDriver.SuccessfulSamples + "\n");
                return;
            }

            var sample = Realtek810xEReadOnlyProbeDriver.LastSample;
            DiagnosticLog.WriteString("[NET3095-RTL810XE] SNAPSHOT=PASS mac=" +
                sample.MacAddress + " link=" + (sample.Carrier ? "up" : "down") +
                " speed=" + sample.SpeedMbps +
                " (register read only; TX/RX/DMA/IRQ not tested)\n");
        }

        protected override void Run()
        {
            Thread.Sleep(1000);
            heartbeat++;
            DiagnosticLog.WriteString("[NET3095-RTL810XE] HEARTBEAT=" + heartbeat + "\n");
        }
    }
}
#endif
