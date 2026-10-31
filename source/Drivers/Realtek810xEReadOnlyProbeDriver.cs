#if ZONDERQ_REALTEK_READONLY_MMIO
using System;
using Cosmos.Kernel.HAL.DriverKit;
using Cosmos.Kernel.HAL.DriverKit.Buses.Pci;
using Cosmos.Kernel.HAL.DriverKit.Resources;
using ZonderqOS.Hardware;

namespace ZonderqOS.Drivers
{
    /// <summary>
    /// Deliberately non-binding, opt-in PCI probe for the ASUS RTL810xE
    /// 10EC:8136 rev06, subsystem 1043:200F. Samples eleven MMIO bytes.
    /// No explicit PCI config writes, bus mastering, DMA, IRQ or network
    /// publication. Never included in the normal disk-backed GUI build.
    /// </summary>
    [Driver(Default = false)]
    public sealed class Realtek810xEReadOnlyProbeDriver : Driver
    {
        private const int RegisterBar = 2;
        private const ulong MinimumRegisterBytes = 0x6D;

        private readonly DeviceMatch[] matches =
        {
            new PciMatch(vendorId: 0x10EC, deviceId: 0x8136,
                subsystemVendorId: 0x1043, subsystemId: 0x200F,
                classCode: 0x02, subclass: 0x00, revision: 0x06)
        };

        public static int ProbeOffers { get; private set; }
        public static int SuccessfulSamples { get; private set; }
        public static Realtek810xERegisterSnapshot LastSample { get; private set; }
        // A failure reason is diagnostic metadata, not proof of a mapped BAR.
        public static string LastDeclineReason { get; private set; }

        public override string Name => "RTL810xEReadOnlyProbe";
        public override ReadOnlySpan<DeviceMatch> Matches => matches;

        private static ProbeResult Decline(string reason)
        {
            LastDeclineReason = reason;
            return ProbeResult.Declined(reason);
        }

        public override ProbeResult Probe(DeviceBinding binding)
        {
            ProbeOffers++;
            LastDeclineReason = null;
            try
            {
                PciAccess pci = binding.Node.Access<PciAccess>();

                // DriverKit owns the PCI node. Do not enable memory decoding
                // or transition D3->D0 in a read-only diagnostic.
                if ((pci.ReadConfig16(0x04) & 0x0002) == 0)
                    return Decline("PCI memory decoding disabled; no MMIO reads");

                byte pm = pci.FindCapability(0x01);
                if (pm != 0 && (pci.ReadConfig16((ushort)(pm + 4)) & 0x0003) != 0)
                    return Decline("PCI device not in D0; no MMIO reads");

                PciBar bar = pci.Bars[RegisterBar];
                if (!bar.IsAssigned || bar.IsIo || bar.Length < MinimumRegisterBytes)
                    return Decline("BAR2 is not an assigned MMIO window of at least 0x6D bytes");

                RegisterWindow window = binding.MapRegisters(RegisterBar);
                if (window.Length < MinimumRegisterBytes)
                    return Decline("mapped BAR2 window too short");

                if (!Realtek810xERegisterSampler.TrySample(
                        new WindowReader(window), out var snapshot))
                    return Decline("invalid MAC/PHY snapshot; no link proof");

                binding.Log("[NET3095-RTL810XE] MMIO_SNAPSHOT=READ mac=" +
                    snapshot.MacAddress + " link=" + (snapshot.Carrier ? "up" : "down") +
                    " speed=" + snapshot.SpeedMbps + " duplex=" +
                    (!snapshot.Carrier ? "unknown" : snapshot.FullDuplex ? "full" : "half") +
                    " phy=0x" + snapshot.RawPhyStatus.ToString("X2") +
                    " txconfig=0x" + snapshot.RawTxConfig.ToString("X8") +
                    " xid=0x" + snapshot.Xid.ToString("X3") +
                    " variant=" + snapshot.ChipVariant +
                    " (read-only, no TX/RX/DMA/IRQ)");
                // Only publish a successful sample after the diagnostic
                // marker itself has been emitted without throwing.
                LastSample = snapshot;
                SuccessfulSamples++;

                // DriverKit releases the mapping after Declined. No NIC is
                // bound and no interface is published by this diagnostic.
                return ProbeResult.Declined("read-only register snapshot; NIC not bound");
            }
            catch (Exception ex)
            {
                // Malformed PCI capability lists, inaccessible BAR mappings
                // and failed MMIO access must not crash the diagnostic boot.
                // Report only the exception type; never report a sample.
                return Decline("PCI/MMIO probe exception: " + ex.GetType().Name);
            }
        }

        private sealed class WindowReader : IRealtek810xEByteReader
        {
            private readonly RegisterWindow window;
            public WindowReader(RegisterWindow window) { this.window = window; }
            public byte Read8(ushort offset) => window.Read8(offset);
        }
    }
}
#endif
