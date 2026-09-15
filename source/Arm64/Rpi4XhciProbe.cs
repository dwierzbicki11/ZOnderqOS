using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;

namespace ZonderqOS
{
    /// <summary>
    /// Raspberry Pi 4 BCM2711 PCI + VL805/xHCI bring-up probe.
    ///
    /// RPi4 does not expose ordinary ECAM configuration space. PFTF/EDK2 uses
    /// the BCM2711 config-space quirk: select a BDF through CFG_INDEX at
    /// PCIE_REG_BASE + 0x9000, then access that function's 4 KiB config page at
    /// PCIE_REG_BASE + 0x8000.
    ///
    /// This stage enables PCI Memory Space decoding for the already-configured
    /// VL805 BAR, then reads xHCI registers using 32-bit accesses only. IRQs,
    /// DMA, controller reset, rings, USB enumeration and keyboard input remain off.
    /// </summary>
    internal static unsafe class Rpi4XhciProbe
    {
        // PFTF RPi4 platform constants (BCM2711).
        private const ulong PcieRegPhysicalBase = 0xFD500000UL;
        private const ulong PcieCpuMmioWindow = 0x600000000UL;
        private const ulong PcieBusMmioBase = 0xF8000000UL;
        private const ulong PcieBusMmioLength = 0x04000000UL;

        private const ulong PcieExtCfgDataOffset = 0x8000UL;
        private const ulong PcieExtCfgIndexOffset = 0x9000UL;
        private const ulong PcieStatusOffset = 0x4068UL;
        private const uint PcieLinkReadyMask = 0x30U;

        // Standard PCI configuration offsets.
        private const ulong PciVendorIdOffset = 0x00UL;
        private const ulong PciDeviceIdOffset = 0x02UL;
        private const ulong PciCommandOffset = 0x04UL;
        private const ulong PciProgIfOffset = 0x09UL;
        private const ulong PciSubclassOffset = 0x0AUL;
        private const ulong PciClassOffset = 0x0BUL;
        private const ulong PciHeaderTypeOffset = 0x0EUL;
        private const ulong PciBar0Offset = 0x10UL;
        private const ulong PciSecondaryBusOffset = 0x19UL;

        private const ushort PciCommandMemorySpace = 0x0002;
        private const ushort InvalidVendorId = 0xFFFF;

        // xHCI PCI class tuple: Serial Bus / USB / xHCI.
        private const byte XhciClassCode = 0x0C;
        private const byte XhciSubclass = 0x03;
        private const byte XhciProgIf = 0x30;

        // xHCI capability / operational register offsets.
        private const ulong HcsParams1Offset = 0x04UL;
        private const ulong HccParams1Offset = 0x10UL;
        private const ulong DoorbellOffset = 0x14UL;
        private const ulong RuntimeOffset = 0x18UL;
        private const ulong OperationalUsbCmdOffset = 0x00UL;
        private const ulong OperationalUsbStsOffset = 0x04UL;
        private const ulong OperationalPageSizeOffset = 0x08UL;
        private const ulong OperationalPortBaseOffset = 0x400UL;
        private const ulong OperationalPortStride = 0x10UL;

        // ARM64 translation-table descriptor bits.
        private const ulong DescriptorValid = 1UL << 0;
        private const ulong DescriptorTable = 1UL << 1;
        private const ulong DescriptorAccessFlag = 1UL << 10;
        private const ulong DescriptorPxn = 1UL << 53;
        private const ulong DescriptorUxn = 1UL << 54;
        private const ulong AddressMask = 0x0000FFFFFFFFF000UL;
        private const ulong Block2MiBAddressMask = 0x0000FFFFFFE00000UL;
        private const ulong Block1GiBAddressMask = 0x0000FFFFC0000000UL;

        public static void Run()
        {
            Console.WriteLine("RPi4 BCM2711 PCI / VL805 xHCI probe:");
            Console.WriteLine("  Cosmos PCI feature: ON");
            Console.WriteLine("  generic PCI init: skipped (IRQs are OFF)");
            Console.WriteLine();

            if (!TryMapDeviceWindow(PcieRegPhysicalBase, out ulong pcieRegs, out string pcieMapResult))
            {
                Console.WriteLine("  PCIe register mapping: FAILED - " + pcieMapResult);
                return;
            }

            Console.WriteLine("  PCIe regs physical: " + Hex(PcieRegPhysicalBase));
            Console.WriteLine("  PCIe regs mapping:  OK - " + pcieMapResult);

            ushort rootVendor = Read16(pcieRegs + PciVendorIdOffset);
            ushort rootDevice = Read16(pcieRegs + PciDeviceIdOffset);
            byte secondaryBus = Read8(pcieRegs + PciSecondaryBusOffset);
            uint linkStatus = Read32(pcieRegs + PcieStatusOffset);

            Console.WriteLine("  root port vendor:   " + Hex(rootVendor));
            Console.WriteLine("  root port device:   " + Hex(rootDevice));
            Console.WriteLine("  secondary bus:      " + secondaryBus);
            Console.WriteLine("  PCIe link status:   " + Hex(linkStatus));

            if ((linkStatus & PcieLinkReadyMask) != PcieLinkReadyMask)
            {
                Console.WriteLine("  PCIe link: NOT READY");
                return;
            }

            Console.WriteLine("  PCIe link: READY");

            if (secondaryBus == 0)
            {
                Console.WriteLine("  downstream bus is zero; refusing to guess a BDF.");
                return;
            }

            // On Raspberry Pi 4 the onboard VL805 is the single endpoint directly
            // behind the root port, so it is function 0, device 0 on the secondary bus.
            ulong vl805Config = SelectConfigFunction(pcieRegs, secondaryBus, 0, 0);
            ushort vendorId = Read16(vl805Config + PciVendorIdOffset);
            ushort deviceId = Read16(vl805Config + PciDeviceIdOffset);

            if (vendorId == InvalidVendorId || vendorId == 0)
            {
                Console.WriteLine("  downstream  " + secondaryBus + ":0.0 vendor: " + Hex(vendorId));
                Console.WriteLine("  VL805 config space not responding.");
                return;
            }

            byte progIf = Read8(vl805Config + PciProgIfOffset);
            byte subclass = Read8(vl805Config + PciSubclassOffset);
            byte classCode = Read8(vl805Config + PciClassOffset);
            byte headerType = Read8(vl805Config + PciHeaderTypeOffset);

            Console.WriteLine();
            Console.WriteLine("  downstream PCI function:");
            Console.WriteLine("    BDF:          " + secondaryBus + ":0.0");
            Console.WriteLine("    vendor:       " + Hex(vendorId));
            Console.WriteLine("    device:       " + Hex(deviceId));
            Console.WriteLine("    class:        " + Hex(classCode));
            Console.WriteLine("    subclass:     " + Hex(subclass));
            Console.WriteLine("    prog-if:      " + Hex(progIf));
            Console.WriteLine("    header type:  " + Hex(headerType));

            if (classCode != XhciClassCode || subclass != XhciSubclass || progIf != XhciProgIf)
            {
                Console.WriteLine("  endpoint is not an xHCI controller; stopping.");
                return;
            }

            uint bar0Low = Read32(vl805Config + PciBar0Offset);
            if ((bar0Low & 1U) != 0)
            {
                Console.WriteLine("  BAR0 is I/O space, not MMIO; stopping.");
                return;
            }

            ulong pciBar = bar0Low & 0xFFFFFFF0U;
            uint barType = (bar0Low >> 1) & 0x3U;
            if (barType == 0x2U)
            {
                uint bar0High = Read32(vl805Config + PciBar0Offset + 4UL);
                pciBar |= ((ulong)bar0High << 32);
            }

            Console.WriteLine("    BAR0 raw:      " + Hex(bar0Low));
            Console.WriteLine("    PCI BAR:       " + Hex(pciBar));

            if (pciBar < PcieBusMmioBase || pciBar >= PcieBusMmioBase + PcieBusMmioLength)
            {
                Console.WriteLine("  BAR0 is outside PFTF's BCM2711 PCI MMIO aperture.");
                return;
            }

            ulong xhciPhysical = PcieCpuMmioWindow + (pciBar - PcieBusMmioBase);
            Console.WriteLine("    CPU MMIO BAR:  " + Hex(xhciPhysical));

            ushort commandBefore = Read16(vl805Config + PciCommandOffset);
            ushort commandWanted = (ushort)(commandBefore | PciCommandMemorySpace);
            if (commandWanted != commandBefore)
            {
                Write16(vl805Config + PciCommandOffset, commandWanted);
                Rpi4PageTableNative.DsbIsb();
            }

            ushort commandAfter = Read16(vl805Config + PciCommandOffset);
            Console.WriteLine("    COMMAND before:" + Hex(commandBefore));
            Console.WriteLine("    COMMAND after: " + Hex(commandAfter));
            Console.WriteLine("    MMIO decode:   " + ((commandAfter & PciCommandMemorySpace) != 0 ? "ON" : "OFF"));

            if ((commandAfter & PciCommandMemorySpace) == 0)
            {
                Console.WriteLine("  could not enable PCI Memory Space decoding.");
                return;
            }

            if (!TryMapDeviceWindow(xhciPhysical, out ulong xhciBase, out string xhciMapResult))
            {
                Console.WriteLine("  xHCI MMIO mapping: FAILED - " + xhciMapResult);
                return;
            }

            Console.WriteLine();
            Console.WriteLine("  xHCI MMIO mapping: OK - " + xhciMapResult);
            Console.WriteLine("  reading xHCI registers with 32-bit accesses...");

            // PFTF's XHC0 _DSM reports RegisterAccessType=1: controller registers
            // must be accessed with 32-bit transfers. CAPLENGTH and HCIVERSION are
            // therefore decoded from the first 32-bit capability dword.
            uint capability0 = Read32(xhciBase);
            byte capLength = (byte)(capability0 & 0xFFU);
            ushort hciVersion = (ushort)((capability0 >> 16) & 0xFFFFU);
            uint hcsParams1 = Read32(xhciBase + HcsParams1Offset);
            uint hccParams1 = Read32(xhciBase + HccParams1Offset);
            uint dbOff = Read32(xhciBase + DoorbellOffset) & 0xFFFFFFFCU;
            uint rtsOff = Read32(xhciBase + RuntimeOffset) & 0xFFFFFFE0U;

            uint maxSlots = hcsParams1 & 0xFFU;
            uint maxInterruptors = (hcsParams1 >> 8) & 0x7FFU;
            uint maxPorts = (hcsParams1 >> 24) & 0xFFU;

            Console.WriteLine("    CAP DWORD0:   " + Hex(capability0));
            Console.WriteLine("    CAPLENGTH:    " + Hex(capLength));
            Console.WriteLine("    HCIVERSION:   " + Hex(hciVersion));
            Console.WriteLine("    HCSPARAMS1:   " + Hex(hcsParams1));
            Console.WriteLine("    HCCPARAMS1:   " + Hex(hccParams1));
            Console.WriteLine("    DBOFF:        " + Hex(dbOff));
            Console.WriteLine("    RTSOFF:       " + Hex(rtsOff));
            Console.WriteLine("    max slots:    " + maxSlots);
            Console.WriteLine("    max intrs:    " + maxInterruptors);
            Console.WriteLine("    max ports:    " + maxPorts);

            if (capLength < 0x20 || capLength > 0x80 || maxSlots == 0 || maxPorts == 0 || maxPorts > 32)
            {
                Console.WriteLine("  xHCI signature sanity check: FAILED");
                return;
            }

            Console.WriteLine("  xHCI capability block: OK");

            ulong operationalBase = xhciBase + capLength;
            uint usbCmd = Read32(operationalBase + OperationalUsbCmdOffset);
            uint usbSts = Read32(operationalBase + OperationalUsbStsOffset);
            uint pageSize = Read32(operationalBase + OperationalPageSizeOffset);

            Console.WriteLine("    USBCMD:       " + Hex(usbCmd));
            Console.WriteLine("    USBSTS:       " + Hex(usbSts));
            Console.WriteLine("    PAGESIZE:     " + Hex(pageSize));

            uint portsToShow = maxPorts;
            if (portsToShow > 8)
                portsToShow = 8;

            Console.WriteLine("    root ports:");
            for (uint port = 0; port < portsToShow; port++)
            {
                ulong portScAddress = operationalBase + OperationalPortBaseOffset + (port * OperationalPortStride);
                uint portSc = Read32(portScAddress);
                bool connected = (portSc & 1U) != 0;

                Console.Write("      port ");
                Console.Write(port + 1);
                Console.Write(": PORTSC=");
                Console.Write(Hex(portSc));
                Console.Write(" connected=");
                Console.WriteLine(connected ? "yes" : "no");
            }

            Console.WriteLine("  BCM2711 PCI + xHCI read-only probe: OK");
        }

        private static ulong SelectConfigFunction(ulong pcieRegs, byte bus, byte device, byte function)
        {
            uint bdf = ((uint)bus << 20) | ((uint)device << 15) | ((uint)function << 12);
            Write32(pcieRegs + PcieExtCfgIndexOffset, bdf);
            Rpi4PageTableNative.DsbIsb();
            return pcieRegs + PcieExtCfgDataOffset;
        }

        /// <summary>
        /// Maps the 1 GiB HHDM region containing a Pi MMIO address as Device memory
        /// when that L1 entry is currently absent. If Limine already provided an L2
        /// table, only the requested 2 MiB block is installed. Existing non-Device
        /// mappings are never replaced.
        ///
        /// Using an L1 Device block for an absent MMIO region avoids consuming Cosmos'
        /// single spare L2 table, allowing both the low BCM2711 PCI registers and the
        /// high 0x600000000 PCI MMIO aperture to coexist in this probe.
        /// </summary>
        private static bool TryMapDeviceWindow(ulong physicalAddress, out ulong virtualAddress, out string result)
        {
            virtualAddress = 0;
            result = string.Empty;

            if (Limine.HHDM.Response == null)
            {
                result = "Limine HHDM response missing";
                return false;
            }

            ulong hhdm = Limine.HHDM.Response->Offset;
            virtualAddress = physicalAddress + hhdm;

            ulong mair = Rpi4PageTableNative.ReadMair();
            int deviceMairIndex = FindDeviceMairIndex(mair);
            if (deviceMairIndex < 0)
            {
                result = "no Device memory MAIR slot";
                return false;
            }

            ulong ttbr1Physical = Rpi4PageTableNative.ReadTtbr1() & AddressMask;
            if (ttbr1Physical == 0)
            {
                result = "TTBR1 physical base is zero";
                return false;
            }

            ulong* l0 = (ulong*)(ttbr1Physical + hhdm);
            int l0Index = (int)((physicalAddress >> 39) & 0x1FFUL);
            ulong l0Entry = l0[l0Index];

            if ((l0Entry & DescriptorValid) == 0 || (l0Entry & DescriptorTable) == 0)
            {
                result = "HHDM L0 entry unavailable";
                return false;
            }

            ulong* l1 = (ulong*)((l0Entry & AddressMask) + hhdm);
            int l1Index = (int)((physicalAddress >> 30) & 0x1FFUL);
            ulong l1Entry = l1[l1Index];

            if ((l1Entry & DescriptorValid) == 0)
            {
                ulong aligned1GiB = physicalAddress & Block1GiBAddressMask;
                l1[l1Index] = BuildDeviceBlockDescriptor(aligned1GiB, deviceMairIndex);
                Rpi4PageTableNative.DsbIsb();
                Rpi4PageTableNative.FlushAllTlb();
                Rpi4PageTableNative.DsbIsb();
                result = "installed 1 GiB Device block";
                return true;
            }

            if ((l1Entry & DescriptorTable) == 0)
            {
                if (DescriptorUsesDeviceMair(l1Entry, mair))
                {
                    result = "existing 1 GiB Device block reused";
                    return true;
                }

                result = "existing L1 block is non-Device memory";
                return false;
            }

            ulong* l2 = (ulong*)((l1Entry & AddressMask) + hhdm);
            ulong aligned2MiB = physicalAddress & Block2MiBAddressMask;
            int l2Index = (int)((aligned2MiB >> 21) & 0x1FFUL);
            ulong l2Entry = l2[l2Index];

            if ((l2Entry & DescriptorValid) != 0)
            {
                if (DescriptorUsesDeviceMair(l2Entry, mair))
                {
                    result = "existing 2 MiB Device block reused";
                    return true;
                }

                result = "existing L2 mapping is non-Device memory";
                return false;
            }

            l2[l2Index] = BuildDeviceBlockDescriptor(aligned2MiB, deviceMairIndex);
            Rpi4PageTableNative.DsbIsb();
            Rpi4PageTableNative.FlushAllTlb();
            Rpi4PageTableNative.DsbIsb();
            result = "installed 2 MiB Device block in existing L2 table";
            return true;
        }

        private static ulong BuildDeviceBlockDescriptor(ulong alignedPhysical, int deviceMairIndex)
        {
            return alignedPhysical
                   | ((ulong)deviceMairIndex << 2)
                   | DescriptorAccessFlag
                   | DescriptorPxn
                   | DescriptorUxn
                   | DescriptorValid;
        }

        private static bool DescriptorUsesDeviceMair(ulong descriptor, ulong mair)
        {
            int index = (int)((descriptor >> 2) & 0x7UL);
            byte attribute = (byte)((mair >> (index * 8)) & 0xFFUL);
            return attribute == 0x00 || attribute == 0x04;
        }

        private static int FindDeviceMairIndex(ulong mair)
        {
            for (int i = 0; i < 8; i++)
            {
                byte attribute = (byte)((mair >> (i * 8)) & 0xFFUL);
                if (attribute == 0x00 || attribute == 0x04)
                    return i;
            }

            return -1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static byte Read8(ulong address) => *(byte*)address;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ushort Read16(ulong address) => *(ushort*)address;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static uint Read32(ulong address) => *(uint*)address;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Write16(ulong address, ushort value) => *(ushort*)address = value;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Write32(ulong address, uint value) => *(uint*)address = value;

        private static string Hex(byte value) => "0x" + value.ToString("X2");
        private static string Hex(ushort value) => "0x" + value.ToString("X4");
        private static string Hex(uint value) => "0x" + value.ToString("X8");
        private static string Hex(ulong value) => "0x" + value.ToString("X");
    }

    internal static partial class Rpi4PageTableNative
    {
        [LibraryImport("*", EntryPoint = "_native_arm64_read_ttbr1_el1")]
        [SuppressGCTransition]
        internal static partial ulong ReadTtbr1();

        [LibraryImport("*", EntryPoint = "_native_arm64_read_mair_el1")]
        [SuppressGCTransition]
        internal static partial ulong ReadMair();

        [LibraryImport("*", EntryPoint = "_native_arm64_tlbi_all")]
        [SuppressGCTransition]
        internal static partial void FlushAllTlb();

        [LibraryImport("*", EntryPoint = "_native_arm64_dsb_isb")]
        [SuppressGCTransition]
        internal static partial void DsbIsb();
    }
}
