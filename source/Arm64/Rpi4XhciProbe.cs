using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;

namespace ZonderqOS
{
    /// <summary>
    /// Read-only Raspberry Pi 4 VL805/xHCI bring-up probe.
    ///
    /// PFTF/EDK2 assigns the VL805 xHCI MMIO BAR to the BCM2711 PCIe CPU
    /// window at physical 0x600000000. Cosmos' current ARM64 DeviceMapper
    /// assumes the containing L1 page-table entry already exists, which is
    /// not true for this high Pi-specific window, so this probe installs one
    /// 2 MiB Device mapping using Cosmos' preallocated spare L2 table.
    ///
    /// This stage deliberately performs no controller writes: it only proves
    /// that the post-UEFI kernel can still see the xHCI capability/port state.
    /// </summary>
    internal static unsafe class Rpi4XhciProbe
    {
        private const ulong XhciPhysicalBase = 0x600000000UL;

        private const ulong DescriptorValid = 1UL << 0;
        private const ulong DescriptorTable = 1UL << 1;
        private const ulong DescriptorAccessFlag = 1UL << 10;
        private const ulong DescriptorPxn = 1UL << 53;
        private const ulong DescriptorUxn = 1UL << 54;
        private const ulong AddressMask = 0x0000FFFFFFFFF000UL;
        private const ulong Block2MiBAddressMask = 0x0000FFFFFFE00000UL;

        private const ulong HcsParams1Offset = 0x04;
        private const ulong HccParams1Offset = 0x10;
        private const ulong DoorbellOffset = 0x14;
        private const ulong RuntimeOffset = 0x18;
        private const ulong OperationalUsbCmdOffset = 0x00;
        private const ulong OperationalUsbStsOffset = 0x04;
        private const ulong OperationalPageSizeOffset = 0x08;
        private const ulong OperationalPortBaseOffset = 0x400;
        private const ulong OperationalPortStride = 0x10;

        public static void Run()
        {
            Console.WriteLine("RPi4 xHCI/VL805 probe (read-only):");
            Console.WriteLine("  physical MMIO: " + Hex(XhciPhysicalBase));

            if (!TryMapDevice2MiB(XhciPhysicalBase, out ulong xhciBase, out string mapResult))
            {
                Console.WriteLine("  MMIO mapping: FAILED - " + mapResult);
                Console.WriteLine("  xHCI registers were NOT touched.");
                return;
            }

            Console.WriteLine("  MMIO mapping: OK - " + mapResult);
            Console.WriteLine("  virtual MMIO:  " + Hex(xhciBase));
            Console.WriteLine("  reading capability registers...");

            byte capLength = Read8(xhciBase);
            ushort hciVersion = Read16(xhciBase + 0x02);
            uint hcsParams1 = Read32(xhciBase + HcsParams1Offset);
            uint hccParams1 = Read32(xhciBase + HccParams1Offset);
            uint dbOff = Read32(xhciBase + DoorbellOffset) & 0xFFFFFFFCU;
            uint rtsOff = Read32(xhciBase + RuntimeOffset) & 0xFFFFFFE0U;

            uint maxSlots = hcsParams1 & 0xFFU;
            uint maxInterruptors = (hcsParams1 >> 8) & 0x7FFU;
            uint maxPorts = (hcsParams1 >> 24) & 0xFFU;

            Console.WriteLine("  CAPLENGTH:   " + Hex(capLength));
            Console.WriteLine("  HCIVERSION:  " + Hex(hciVersion));
            Console.WriteLine("  HCSPARAMS1:  " + Hex(hcsParams1));
            Console.WriteLine("  HCCPARAMS1:  " + Hex(hccParams1));
            Console.WriteLine("  DBOFF:       " + Hex(dbOff));
            Console.WriteLine("  RTSOFF:      " + Hex(rtsOff));
            Console.WriteLine("  max slots:   " + maxSlots);
            Console.WriteLine("  max intrs:   " + maxInterruptors);
            Console.WriteLine("  max ports:   " + maxPorts);

            // xHCI 1.x capability length is normally at least 0x20 and the
            // Raspberry Pi VL805 exposes only a small number of root ports.
            // Refuse to walk operational registers if the first read clearly
            // did not look like a valid xHCI capability block.
            if (capLength < 0x20 || capLength > 0x80 || maxSlots == 0 || maxPorts == 0 || maxPorts > 32)
            {
                Console.WriteLine("  xHCI signature sanity check: FAILED");
                Console.WriteLine("  stopping before operational/port register reads.");
                return;
            }

            Console.WriteLine("  xHCI capability block: OK");

            ulong operationalBase = xhciBase + capLength;
            uint usbCmd = Read32(operationalBase + OperationalUsbCmdOffset);
            uint usbSts = Read32(operationalBase + OperationalUsbStsOffset);
            uint pageSize = Read32(operationalBase + OperationalPageSizeOffset);

            Console.WriteLine("  USBCMD:      " + Hex(usbCmd));
            Console.WriteLine("  USBSTS:      " + Hex(usbSts));
            Console.WriteLine("  PAGESIZE:    " + Hex(pageSize));

            uint portsToShow = maxPorts;
            if (portsToShow > 8)
                portsToShow = 8;

            Console.WriteLine("  root ports:");
            for (uint port = 0; port < portsToShow; port++)
            {
                ulong portScAddress = operationalBase + OperationalPortBaseOffset + (port * OperationalPortStride);
                uint portSc = Read32(portScAddress);
                bool connected = (portSc & 1U) != 0;

                Console.Write("    port ");
                Console.Write(port + 1);
                Console.Write(": PORTSC=");
                Console.Write(Hex(portSc));
                Console.Write(" connected=");
                Console.WriteLine(connected ? "yes" : "no");
            }

            Console.WriteLine("  xHCI read-only probe: OK");
        }

        private static bool TryMapDevice2MiB(ulong physicalAddress, out ulong virtualAddress, out string result)
        {
            virtualAddress = 0;
            result = string.Empty;

            if (Limine.HHDM.Response == null)
            {
                result = "Limine HHDM response missing";
                return false;
            }

            ulong hhdm = Limine.HHDM.Response->Offset;
            ulong alignedPhysical = physicalAddress & Block2MiBAddressMask;
            virtualAddress = physicalAddress + hhdm;

            int deviceMairIndex = FindDeviceMairIndex(Rpi4PageTableNative.ReadMair());
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
            int l0Index = (int)((alignedPhysical >> 39) & 0x1FF);
            ulong l0Entry = l0[l0Index];

            if ((l0Entry & DescriptorValid) == 0 || (l0Entry & DescriptorTable) == 0)
            {
                result = "HHDM L0 entry unavailable";
                return false;
            }

            ulong* l1 = (ulong*)((l0Entry & AddressMask) + hhdm);
            int l1Index = (int)((alignedPhysical >> 30) & 0x1FF);
            ulong l1Entry = l1[l1Index];
            ulong* l2;

            if ((l1Entry & DescriptorValid) == 0)
            {
                ulong spareL2Virtual = Rpi4PageTableNative.GetSpareL2TableAddr();
                if (spareL2Virtual == 0)
                {
                    result = "Cosmos spare L2 table unavailable";
                    return false;
                }

                ulong spareL2Physical = Rpi4PageTableNative.VirtToPhys(spareL2Virtual);
                if (spareL2Physical == 0)
                {
                    result = "cannot translate spare L2 table";
                    return false;
                }

                l2 = (ulong*)spareL2Virtual;
                for (int i = 0; i < 512; i++)
                    l2[i] = 0;

                int l2Index = (int)((alignedPhysical >> 21) & 0x1FF);
                l2[l2Index] = BuildDeviceBlockDescriptor(alignedPhysical, deviceMairIndex);
                Rpi4PageTableNative.DsbIsb();

                l1[l1Index] = (spareL2Physical & AddressMask) | DescriptorValid | DescriptorTable;
                Rpi4PageTableNative.DsbIsb();
                Rpi4PageTableNative.FlushTlb((alignedPhysical + hhdm) >> 12);
                Rpi4PageTableNative.DsbIsb();

                result = "installed new L2 Device mapping";
                return true;
            }

            if ((l1Entry & DescriptorTable) == 0)
            {
                result = "target L1 entry is a 1 GiB block; refusing broad remap";
                return false;
            }

            l2 = (ulong*)((l1Entry & AddressMask) + hhdm);
            int existingL2Index = (int)((alignedPhysical >> 21) & 0x1FF);
            ulong existingL2Entry = l2[existingL2Index];

            if ((existingL2Entry & DescriptorValid) != 0)
            {
                int existingMairIndex = (int)((existingL2Entry >> 2) & 0x7);
                byte existingAttribute = (byte)((Rpi4PageTableNative.ReadMair() >> (existingMairIndex * 8)) & 0xFF);
                if (existingAttribute == 0x00 || existingAttribute == 0x04)
                {
                    result = "existing Device mapping reused";
                    return true;
                }

                result = "target already mapped as non-Device memory";
                return false;
            }

            l2[existingL2Index] = BuildDeviceBlockDescriptor(alignedPhysical, deviceMairIndex);
            Rpi4PageTableNative.DsbIsb();
            Rpi4PageTableNative.FlushTlb((alignedPhysical + hhdm) >> 12);
            Rpi4PageTableNative.DsbIsb();

            result = "added Device entry to existing L2 table";
            return true;
        }

        private static ulong BuildDeviceBlockDescriptor(ulong alignedPhysical, int deviceMairIndex)
        {
            return (alignedPhysical & Block2MiBAddressMask)
                   | ((ulong)deviceMairIndex << 2)
                   | DescriptorAccessFlag
                   | DescriptorPxn
                   | DescriptorUxn
                   | DescriptorValid;
        }

        private static int FindDeviceMairIndex(ulong mair)
        {
            for (int i = 0; i < 8; i++)
            {
                byte attribute = (byte)((mair >> (i * 8)) & 0xFF);
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

        private static string Hex(byte value) => "0x" + value.ToString("X2");
        private static string Hex(ushort value) => "0x" + value.ToString("X4");
        private static string Hex(uint value) => "0x" + value.ToString("X8");
        private static string Hex(ulong value) => "0x" + value.ToString("X");
    }

    /// <summary>
    /// User-kernel declarations for the native ARM64 page-table helpers that
    /// Cosmos itself uses behind DeviceMapper. Keeping them local lets the Pi
    /// probe handle a previously-unmapped high MMIO window without making the
    /// whole generic ARM64 HAL/IRQ path active.
    /// </summary>
    internal static partial class Rpi4PageTableNative
    {
        [LibraryImport("*", EntryPoint = "_native_arm64_read_ttbr1_el1")]
        [SuppressGCTransition]
        internal static partial ulong ReadTtbr1();

        [LibraryImport("*", EntryPoint = "_native_arm64_read_mair_el1")]
        [SuppressGCTransition]
        internal static partial ulong ReadMair();

        [LibraryImport("*", EntryPoint = "_native_arm64_tlbi_vale1")]
        [SuppressGCTransition]
        internal static partial void FlushTlb(ulong vaShifted);

        [LibraryImport("*", EntryPoint = "_native_arm64_va_to_pa")]
        [SuppressGCTransition]
        internal static partial ulong VirtToPhys(ulong virtualAddress);

        [LibraryImport("*", EntryPoint = "_native_arm64_spare_l2_table_addr")]
        [SuppressGCTransition]
        internal static partial ulong GetSpareL2TableAddr();

        [LibraryImport("*", EntryPoint = "_native_arm64_dsb_isb")]
        [SuppressGCTransition]
        internal static partial void DsbIsb();
    }
}
