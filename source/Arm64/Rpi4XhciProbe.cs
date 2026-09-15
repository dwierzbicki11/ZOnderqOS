using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.ARM64.Cpu;

namespace ZonderqOS
{
    /// <summary>
    /// Raspberry Pi 4 BCM2711 PCI + VL805/xHCI bring-up probe.
    ///
    /// BCM2711 PCI configuration space is not ordinary ECAM. PFTF selects a BDF
    /// through PCIE_REG_BASE + 0x9000 and exposes the selected 4 KiB config page at
    /// PCIE_REG_BASE + 0x8000. This probe follows that firmware quirk directly.
    ///
    /// MMIO mappings are delegated to Cosmos 3.0.84 DeviceMapper first. The probe
    /// verifies the resulting page-table attribute before touching the device. The
    /// only local fallback is for the Pi-specific high PCIe CPU aperture when its L1
    /// entry is completely absent; existing mappings are never replaced by fallback.
    ///
    /// The probe may enable PCI Memory Space decoding. It does not reset or start
    /// xHCI, allocate rings, enumerate USB, install HID, or enable xHCI interrupts.
    /// </summary>
    internal static unsafe class Rpi4XhciProbe
    {
        // PFTF / BCM2711 platform constants.
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
        private const ushort PciCommandBusMaster = 0x0004;
        private const ushort InvalidVendorId = 0xFFFF;

        // xHCI PCI class tuple: Serial Bus / USB / xHCI.
        private const byte XhciClassCode = 0x0C;
        private const byte XhciSubclass = 0x03;
        private const byte XhciProgIf = 0x30;

        // xHCI capability / operational registers.
        private const ulong HcsParams1Offset = 0x04UL;
        private const ulong HccParams1Offset = 0x10UL;
        private const ulong DoorbellOffset = 0x14UL;
        private const ulong RuntimeOffset = 0x18UL;
        private const ulong OperationalUsbCmdOffset = 0x00UL;
        private const ulong OperationalUsbStsOffset = 0x04UL;
        private const ulong OperationalPageSizeOffset = 0x08UL;
        private const ulong OperationalPortBaseOffset = 0x400UL;
        private const ulong OperationalPortStride = 0x10UL;

        // ARM64 translation table bits, used only to verify DeviceMapper and for the
        // missing-L1 fallback in the dedicated high PCIe CPU aperture.
        private const ulong DescriptorValid = 1UL << 0;
        private const ulong DescriptorTable = 1UL << 1;
        private const ulong DescriptorAccessFlag = 1UL << 10;
        private const ulong DescriptorPxn = 1UL << 53;
        private const ulong DescriptorUxn = 1UL << 54;
        private const ulong AddressMask = 0x0000FFFFFFFFF000UL;
        private const ulong Block1GiBAddressMask = 0x0000FFFFC0000000UL;

        public static void Run()
        {
            Console.WriteLine("RPi4 BCM2711 PCI / VL805 xHCI probe:");
            Console.WriteLine("  IRQ/GIC init path:       managed shell reached");
            Console.WriteLine("  Cosmos generic PCI init: OFF (explicit ARM64 setting)");
            Console.WriteLine("  BCM2711 manual PCI cfg:  ON");
            Console.WriteLine("  MMIO mapper:             Cosmos DeviceMapper + verified Pi fallback");
            Console.WriteLine();

            Console.WriteLine("[1/5] Mapping BCM2711 PCIe registers...");
            if (!EnsureDeviceMapped(PcieRegPhysicalBase, out ulong pcieRegs, out string pcieMapResult))
            {
                Console.WriteLine("  PCIe register mapping: FAILED");
                Console.WriteLine("  detail: " + pcieMapResult);
                return;
            }

            Console.WriteLine("  physical: " + Hex(PcieRegPhysicalBase));
            Console.WriteLine("  mapping:  OK - " + pcieMapResult);

            Console.WriteLine();
            Console.WriteLine("[2/5] Checking BCM2711 root port...");

            ushort rootVendor = Read16(pcieRegs + PciVendorIdOffset);
            ushort rootDevice = Read16(pcieRegs + PciDeviceIdOffset);
            byte secondaryBus = Read8(pcieRegs + PciSecondaryBusOffset);
            uint linkStatus = Read32(pcieRegs + PcieStatusOffset);

            Console.WriteLine("  root vendor:   " + Hex(rootVendor));
            Console.WriteLine("  root device:   " + Hex(rootDevice));
            Console.WriteLine("  secondary bus: " + secondaryBus);
            Console.WriteLine("  link status:   " + Hex(linkStatus));

            if ((linkStatus & PcieLinkReadyMask) != PcieLinkReadyMask)
            {
                Console.WriteLine("  PCIe link: NOT READY");
                return;
            }

            if (secondaryBus == 0)
            {
                Console.WriteLine("  PCIe link: READY, but secondary bus is zero; stopping.");
                return;
            }

            Console.WriteLine("  PCIe link: READY");

            Console.WriteLine();
            Console.WriteLine("[3/5] Reading downstream VL805 PCI function...");

            // The onboard VL805 is the single endpoint directly behind the BCM2711
            // root port: device 0, function 0 on the bridge secondary bus.
            ulong vl805Config = SelectConfigFunction(pcieRegs, secondaryBus, 0, 0);
            ushort vendorId = Read16(vl805Config + PciVendorIdOffset);
            ushort deviceId = Read16(vl805Config + PciDeviceIdOffset);

            if (vendorId == InvalidVendorId || vendorId == 0)
            {
                Console.WriteLine("  BDF:    " + secondaryBus + ":0.0");
                Console.WriteLine("  vendor: " + Hex(vendorId));
                Console.WriteLine("  downstream PCI config space not responding.");
                return;
            }

            byte progIf = Read8(vl805Config + PciProgIfOffset);
            byte subclass = Read8(vl805Config + PciSubclassOffset);
            byte classCode = Read8(vl805Config + PciClassOffset);
            byte headerType = Read8(vl805Config + PciHeaderTypeOffset);

            Console.WriteLine("  BDF:         " + secondaryBus + ":0.0");
            Console.WriteLine("  vendor:      " + Hex(vendorId));
            Console.WriteLine("  device:      " + Hex(deviceId));
            Console.WriteLine("  class:       " + Hex(classCode));
            Console.WriteLine("  subclass:    " + Hex(subclass));
            Console.WriteLine("  prog-if:     " + Hex(progIf));
            Console.WriteLine("  header type: " + Hex(headerType));

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

            uint barType = (bar0Low >> 1) & 0x3U;
            if (barType == 0x3U)
            {
                Console.WriteLine("  BAR0 uses reserved PCI memory type; stopping.");
                return;
            }

            ulong pciBar = bar0Low & 0xFFFFFFF0U;
            if (barType == 0x2U)
            {
                uint bar0High = Read32(vl805Config + PciBar0Offset + 4UL);
                pciBar |= (ulong)bar0High << 32;
            }

            Console.WriteLine("  BAR0 raw: " + Hex(bar0Low));
            Console.WriteLine("  PCI BAR:  " + Hex(pciBar));

            if (pciBar == 0)
            {
                Console.WriteLine("  BAR0 is unassigned; stopping.");
                return;
            }

            if (pciBar < PcieBusMmioBase || pciBar >= PcieBusMmioBase + PcieBusMmioLength)
            {
                Console.WriteLine("  BAR0 is outside PFTF's BCM2711 PCI MMIO aperture.");
                return;
            }

            ulong xhciPhysical = PcieCpuMmioWindow + (pciBar - PcieBusMmioBase);
            Console.WriteLine("  CPU MMIO: " + Hex(xhciPhysical));

            ushort commandBefore = Read16(vl805Config + PciCommandOffset);
            ushort commandWanted = (ushort)(commandBefore | PciCommandMemorySpace);
            if (commandWanted != commandBefore)
            {
                Write16(vl805Config + PciCommandOffset, commandWanted);
                Rpi4PageTableNative.DsbIsb();
            }

            ushort commandAfter = Read16(vl805Config + PciCommandOffset);
            Console.WriteLine("  COMMAND before: " + Hex(commandBefore));
            Console.WriteLine("  COMMAND after:  " + Hex(commandAfter));
            Console.WriteLine("  MMIO decode:    " + ((commandAfter & PciCommandMemorySpace) != 0 ? "ON" : "OFF"));
            Console.WriteLine("  bus master:     " + ((commandAfter & PciCommandBusMaster) != 0 ? "ON (preserved)" : "OFF"));

            if ((commandAfter & PciCommandMemorySpace) == 0)
            {
                Console.WriteLine("  could not enable PCI Memory Space decoding.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("[4/5] Mapping VL805 xHCI MMIO BAR...");
            if (!EnsureDeviceMapped(xhciPhysical, out ulong xhciBase, out string xhciMapResult))
            {
                Console.WriteLine("  xHCI MMIO mapping: FAILED");
                Console.WriteLine("  detail: " + xhciMapResult);
                return;
            }

            Console.WriteLine("  mapping: OK - " + xhciMapResult);

            Console.WriteLine();
            Console.WriteLine("[5/5] Reading xHCI capability block (32-bit accesses only)...");

            // PFTF XHC0 _DSM reports RegisterAccessType=1, so decode CAPLENGTH and
            // HCIVERSION from one 32-bit access instead of byte/halfword MMIO reads.
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

            Console.WriteLine("  CAP DWORD0:  " + Hex(capability0));
            Console.WriteLine("  CAPLENGTH:   " + Hex(capLength));
            Console.WriteLine("  HCIVERSION:  " + Hex(hciVersion));
            Console.WriteLine("  HCSPARAMS1:  " + Hex(hcsParams1));
            Console.WriteLine("  HCCPARAMS1:  " + Hex(hccParams1));
            Console.WriteLine("  DBOFF:       " + Hex(dbOff));
            Console.WriteLine("  RTSOFF:      " + Hex(rtsOff));
            Console.WriteLine("  max slots:   " + maxSlots);
            Console.WriteLine("  max intrs:   " + maxInterruptors);
            Console.WriteLine("  max ports:   " + maxPorts);

            if (capLength < 0x20 || capLength > 0x80 ||
                hciVersion < 0x0090 || hciVersion > 0x0200 ||
                maxSlots == 0 || maxPorts == 0 || maxPorts > 32)
            {
                Console.WriteLine("  xHCI signature sanity check: FAILED");
                Console.WriteLine("  no operational or port registers will be touched.");
                return;
            }

            Console.WriteLine("  xHCI capability block: OK");

            ulong operationalBase = xhciBase + capLength;
            uint usbCmd = Read32(operationalBase + OperationalUsbCmdOffset);
            uint usbSts = Read32(operationalBase + OperationalUsbStsOffset);
            uint pageSize = Read32(operationalBase + OperationalPageSizeOffset);

            Console.WriteLine("  USBCMD:   " + Hex(usbCmd));
            Console.WriteLine("  USBSTS:   " + Hex(usbSts));
            Console.WriteLine("  PAGESIZE: " + Hex(pageSize));

            uint portsToShow = maxPorts > 8 ? 8U : maxPorts;
            Console.WriteLine("  root ports:");
            for (uint port = 0; port < portsToShow; port++)
            {
                ulong portScAddress = operationalBase + OperationalPortBaseOffset + port * OperationalPortStride;
                uint portSc = Read32(portScAddress);
                Console.Write("    port ");
                Console.Write(port + 1);
                Console.Write(": PORTSC=");
                Console.Write(Hex(portSc));
                Console.Write(" connected=");
                Console.WriteLine((portSc & 1U) != 0 ? "yes" : "no");
            }

            Console.WriteLine();
            Console.WriteLine("RESULT: BCM2711 PCI config and VL805 xHCI MMIO are reachable.");
            Console.WriteLine("xHCI remains read-only; no reset, rings, USB enumeration or HID yet.");
        }

        private static ulong SelectConfigFunction(ulong pcieRegs, byte bus, byte device, byte function)
        {
            uint bdf = ((uint)bus << 20) | ((uint)device << 15) | ((uint)function << 12);
            Write32(pcieRegs + PcieExtCfgIndexOffset, bdf);
            Rpi4PageTableNative.DsbIsb();
            return pcieRegs + PcieExtCfgDataOffset;
        }

        private static bool EnsureDeviceMapped(ulong physicalAddress, out ulong virtualAddress, out string result)
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

            // Use the upstream Cosmos mapper first. In 3.0.84 this performs ARM64
            // Break-Before-Make when a valid L2 block exists with Normal attributes.
            DeviceMapper.EnsureMapped(physicalAddress);

            if (!TryInspectMapping(physicalAddress, out bool isDevice, out bool l1Missing, out string detail))
            {
                result = "cannot inspect mapping after DeviceMapper: " + detail;
                return false;
            }

            if (isDevice)
            {
                result = "Cosmos DeviceMapper verified; " + detail;
                return true;
            }

            // Upstream DeviceMapper 3.0.84 cannot create a completely absent L1.
            // Permit one narrow fallback policy: only a target inside PFTF's known
            // high PCIe CPU aperture, and only when the L1 entry is invalid. We map
            // the containing 1 GiB VA block as Device; no valid mapping is replaced.
            if (l1Missing && IsInsidePcieCpuAperture(physicalAddress))
            {
                if (!TryInstallHighPcieL1DeviceBlock(physicalAddress, out string fallbackDetail))
                {
                    result = detail + "; high-window fallback failed: " + fallbackDetail;
                    return false;
                }

                if (!TryInspectMapping(physicalAddress, out isDevice, out _, out string verifyDetail) || !isDevice)
                {
                    result = "high-window fallback installed but verification failed: " + verifyDetail;
                    return false;
                }

                result = "Pi high-window fallback verified; " + verifyDetail;
                return true;
            }

            result = "mapping is not Device after Cosmos DeviceMapper; " + detail;
            return false;
        }

        private static bool TryInspectMapping(
            ulong physicalAddress,
            out bool isDevice,
            out bool l1Missing,
            out string detail)
        {
            isDevice = false;
            l1Missing = false;
            detail = string.Empty;

            if (Limine.HHDM.Response == null)
            {
                detail = "HHDM missing";
                return false;
            }

            ulong hhdm = Limine.HHDM.Response->Offset;
            ulong mair = Rpi4PageTableNative.ReadMair();
            ulong ttbr1Physical = Rpi4PageTableNative.ReadTtbr1() & AddressMask;
            if (ttbr1Physical == 0)
            {
                detail = "TTBR1 physical base is zero";
                return false;
            }

            ulong* l0 = (ulong*)(ttbr1Physical + hhdm);
            int l0Index = (int)((physicalAddress >> 39) & 0x1FFUL);
            ulong l0Entry = l0[l0Index];
            if ((l0Entry & DescriptorValid) == 0 || (l0Entry & DescriptorTable) == 0)
            {
                detail = "L0 entry unavailable";
                return false;
            }

            ulong* l1 = (ulong*)((l0Entry & AddressMask) + hhdm);
            int l1Index = (int)((physicalAddress >> 30) & 0x1FFUL);
            ulong l1Entry = l1[l1Index];

            if ((l1Entry & DescriptorValid) == 0)
            {
                l1Missing = true;
                detail = "L1 entry is absent";
                return true;
            }

            if ((l1Entry & DescriptorTable) == 0)
            {
                byte attr = GetMairAttribute(l1Entry, mair);
                isDevice = IsDeviceMair(attr);
                detail = "L1 block MAIR=" + Hex(attr) + (isDevice ? " Device" : " Normal/non-Device");
                return true;
            }

            ulong* l2 = (ulong*)((l1Entry & AddressMask) + hhdm);
            int l2Index = (int)((physicalAddress >> 21) & 0x1FFUL);
            ulong l2Entry = l2[l2Index];

            if ((l2Entry & DescriptorValid) == 0)
            {
                detail = "L2 entry is absent";
                return true;
            }

            if ((l2Entry & DescriptorTable) != 0)
            {
                detail = "L2 entry is a table; 4 KiB mapping verification is not implemented";
                return true;
            }

            byte l2Attr = GetMairAttribute(l2Entry, mair);
            isDevice = IsDeviceMair(l2Attr);
            detail = "L2 block MAIR=" + Hex(l2Attr) + (isDevice ? " Device" : " Normal/non-Device");
            return true;
        }

        private static bool TryInstallHighPcieL1DeviceBlock(ulong physicalAddress, out string result)
        {
            result = string.Empty;

            if (!IsInsidePcieCpuAperture(physicalAddress))
            {
                result = "target is outside the PFTF PCIe CPU aperture";
                return false;
            }

            if (Limine.HHDM.Response == null)
            {
                result = "HHDM missing";
                return false;
            }

            ulong hhdm = Limine.HHDM.Response->Offset;
            ulong mair = Rpi4PageTableNative.ReadMair();
            int deviceMairIndex = FindDeviceMairIndex(mair);
            if (deviceMairIndex < 0)
            {
                result = "no Device MAIR slot";
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
                result = "L0 entry unavailable";
                return false;
            }

            ulong* l1 = (ulong*)((l0Entry & AddressMask) + hhdm);
            int l1Index = (int)((physicalAddress >> 30) & 0x1FFUL);
            if ((l1[l1Index] & DescriptorValid) != 0)
            {
                result = "L1 became valid; refusing fallback overwrite";
                return false;
            }

            ulong aligned1GiB = physicalAddress & Block1GiBAddressMask;
            ulong descriptor = aligned1GiB
                               | ((ulong)deviceMairIndex << 2)
                               | DescriptorAccessFlag
                               | DescriptorPxn
                               | DescriptorUxn
                               | DescriptorValid;

            l1[l1Index] = descriptor;
            Rpi4PageTableNative.DsbIsb();
            Rpi4PageTableNative.FlushAllTlb();
            Rpi4PageTableNative.DsbIsb();

            result = "installed absent high-aperture L1 Device block";
            return true;
        }

        private static bool IsInsidePcieCpuAperture(ulong physicalAddress)
        {
            return physicalAddress >= PcieCpuMmioWindow &&
                   physicalAddress < PcieCpuMmioWindow + PcieBusMmioLength;
        }

        private static byte GetMairAttribute(ulong descriptor, ulong mair)
        {
            int index = (int)((descriptor >> 2) & 0x7UL);
            return (byte)((mair >> (index * 8)) & 0xFFUL);
        }

        private static bool IsDeviceMair(byte attribute)
        {
            return attribute == 0x00 || attribute == 0x04;
        }

        private static int FindDeviceMairIndex(ulong mair)
        {
            for (int i = 0; i < 8; i++)
            {
                byte attribute = (byte)((mair >> (i * 8)) & 0xFFUL);
                if (IsDeviceMair(attribute))
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

    /// <summary>
    /// Minimal native imports used only to verify Cosmos' ARM64 page-table mapping
    /// and to create an otherwise-absent L1 mapping for the known Pi PCIe aperture.
    /// </summary>
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
