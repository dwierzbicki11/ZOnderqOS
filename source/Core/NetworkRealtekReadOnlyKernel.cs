#if ZONDERQ_REALTEK_READONLY_MMIO
using System;
using System.Threading;
using DiagnosticLog = Cosmos.Kernel.System.Diagnostics.Log;
using Sys = Cosmos.Kernel.System;
using ZonderqOS.Drivers;

namespace ZonderqOS
{
    /// <summary>
    /// Isolated hardware inspection kernel for ASUS RTL810xE.
    /// No VFS, GUI, accounts, network protocol stack or host disk writes.
    /// A real snapshot requires the exact PCI function and a mapped BAR2.
    /// </summary>
    public sealed class NetworkRealtekReadOnlyKernel : Sys.Kernel
    {
        private static int heartbeat;

        // A laptop may not expose an accessible UART connector. Mirror the
        // one-time result to the text console for a photograph, but never
        // let a missing video console invalidate the original serial proof.
        // Do not print the periodic heartbeat here: preserve the result on
        // screen without scrolling it away.
        private static void Display(string message)
        {
            try
            {
                Console.WriteLine(message);
            }
            catch (Exception)
            {
                // The serial log remains the authoritative machine-readable
                // record when the firmware/video console is unavailable.
            }
        }

        protected override void BeforeRun()
        {
            DiagnosticLog.WriteString("[NET3095-RTL810XE] BOOT read-only physical probe\n");
            Display("ZOnderqOS 3.0.95 / RTL810xE READ-ONLY");
            Display("No disks or Ethernet TX/RX/DMA/IRQ.");
            Display("Physical ASUS 10EC:8136 rev06 probe");
            if (Realtek810xEReadOnlyProbeDriver.ProbeOffers != 1 ||
                Realtek810xEReadOnlyProbeDriver.SuccessfulSamples != 1)
            {
                string reason = Realtek810xEReadOnlyProbeDriver.LastDeclineReason ?? "no matching PCI probe offer";
                DiagnosticLog.WriteString("[NET3095-RTL810XE][FAIL] no unique valid MMIO snapshot; offers=" +
                    Realtek810xEReadOnlyProbeDriver.ProbeOffers +
                    " samples=" + Realtek810xEReadOnlyProbeDriver.SuccessfulSamples +
                    " reason=" + reason + "\n");
                Display("PROBE FAILED / NO NIC BOUND");
                Display("Offers: " + Realtek810xEReadOnlyProbeDriver.ProbeOffers +
                    "  samples: " + Realtek810xEReadOnlyProbeDriver.SuccessfulSamples);
                Display("Reason: " + reason);
                Display("Photograph this screen for diagnosis.");
                return;
            }

            var sample = Realtek810xEReadOnlyProbeDriver.LastSample;
            DiagnosticLog.WriteString("[NET3095-RTL810XE] SNAPSHOT=PASS mac=" +
                sample.MacAddress + " link=" + (sample.Carrier ? "up" : "down") +
                " speed=" + sample.SpeedMbps +
                " duplex=" + (!sample.Carrier ? "unknown" : sample.FullDuplex ? "full" : "half") +
                " phy=0x" + sample.RawPhyStatus.ToString("X2") +
                " txconfig=0x" + sample.RawTxConfig.ToString("X8") +
                " xid=0x" + sample.Xid.ToString("X3") +
                " variant=" + sample.ChipVariant +
                " samples=2 (register read only; TX/RX/DMA/IRQ not tested)\n");
            Display("REGISTER SNAPSHOT / NOT A NETWORK TEST");
            Display("MAC: " + sample.MacAddress);
            Display("Link: " + (sample.Carrier ? "UP" : "DOWN") +
                "  speed: " + sample.SpeedMbps + " Mb/s");
            Display("Duplex: " + (!sample.Carrier ? "unknown" :
                sample.FullDuplex ? "full" : "half"));
            Display("PHY: 0x" + sample.RawPhyStatus.ToString("X2") +
                "  TxConfig: 0x" + sample.RawTxConfig.ToString("X8"));
            Display("XID: 0x" + sample.Xid.ToString("X3") +
                "  chip: " + sample.ChipVariant);
            Display("2 stable reads. TX/RX/DMA/IRQ untested.");
            Display("Photograph this screen for diagnosis.");
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
