using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.ARM64.Cpu;

namespace ZonderqOS
{
    internal static unsafe class Rpi4XhciResetStage
    {
        private const ulong PcieRegPhysicalBase = 0xFD500000UL;
        private const ulong PcieCpuMmioWindow = 0x600000000UL;
        private const ulong PcieBusMmioBase = 0xF8000000UL;
        private const ulong PcieBusMmioLength = 0x04000000UL;
        private const ulong PcieExtCfgDataOffset = 0x8000UL;
        private const ulong PcieExtCfgIndexOffset = 0x9000UL;

        private const ulong PciCommandOffset = 0x04UL;
        private const ulong PciBar0Offset = 0x10UL;
        private const ulong PciSecondaryBusOffset = 0x19UL;
        private const ushort PciCommandMemorySpace = 0x0002;

        private const ulong HccParams1Offset = 0x10UL;
        private const ulong OperationalUsbCmdOffset = 0x00UL;
        private const ulong OperationalUsbStsOffset = 0x04UL;
        private const ulong OperationalPageSizeOffset = 0x08UL;
        private const ulong OperationalConfigOffset = 0x38UL;

        private const uint UsbCmdRun = 1U << 0;
        private const uint UsbCmdHostControllerReset = 1U << 1;
        private const uint UsbStsHalted = 1U << 0;
        private const uint UsbStsControllerNotReady = 1U << 11;
        private const uint UsbStsHostControllerError = 1U << 12;

        private const byte XhciExtCapLegacySupport = 1;
        private const uint LegacyBiosOwned = 1U << 16;
        private const uint LegacyOsOwned = 1U << 24;

        private const ulong AddressMask = 0x0000FFFFFFFFF000UL;
        private const ulong DescriptorValid = 1UL << 0;
        private const ulong DescriptorTable = 1UL << 1;

        public static bool Run()
        {
            Console.WriteLine("RPi4 xHCI Stage 1 - ownership + controller reset:");
            Console.WriteLine("  DMA/rings:       OFF");
            Console.WriteLine("  USB enumeration: OFF");
            Console.WriteLine("  HID keyboard:    OFF");
            Console.WriteLine();

            if (!TryLocateController(out ulong xhciBase, out ulong operationalBase, out string locateDetail))
            {
                Console.WriteLine("  controller locate: FAILED");
                Console.WriteLine("  detail: " + locateDetail);
                return false;
            }

            Console.WriteLine("  controller locate: OK - " + locateDetail);

            if (!TryTakeLegacyOwnership(xhciBase, out string ownershipDetail))
            {
                Console.WriteLine("  legacy ownership: FAILED");
                Console.WriteLine("  detail: " + ownershipDetail);
                return false;
            }

            Console.WriteLine("  legacy ownership: " + ownershipDetail);

            uint usbCmdBefore = Read32(operationalBase + OperationalUsbCmdOffset);
            uint usbStsBefore = Read32(operationalBase + OperationalUsbStsOffset);
            Console.WriteLine("  USBCMD before: " + Hex(usbCmdBefore));
            Console.WriteLine("  USBSTS before: " + Hex(usbStsBefore));

            if ((usbStsBefore & UsbStsHostControllerError) != 0)
            {
                Console.WriteLine("  controller already reports HCE; refusing reset stage.");
                return false;
            }

            Console.WriteLine("  halting controller...");
            if ((usbStsBefore & UsbStsHalted) == 0)
            {
                Write32(operationalBase + OperationalUsbCmdOffset, usbCmdBefore & ~UsbCmdRun);
                Rpi4PageTableNative.DsbIsb();

                if (!WaitRegisterBits(operationalBase + OperationalUsbStsOffset, UsbStsHalted, UsbStsHalted, 1_000_000UL, out uint haltStatus))
                {
                    Console.WriteLine("  controller halt: TIMEOUT, USBSTS=" + Hex(haltStatus));
                    return false;
                }
            }

            Console.WriteLine("  controller halt: OK");

            uint haltedCmd = Read32(operationalBase + OperationalUsbCmdOffset);
            Console.WriteLine("  issuing HCRST...");
            Write32(operationalBase + OperationalUsbCmdOffset, (haltedCmd & ~UsbCmdRun) | UsbCmdHostControllerReset);
            Rpi4PageTableNative.DsbIsb();

            if (!WaitRegisterBits(operationalBase + OperationalUsbCmdOffset, UsbCmdHostControllerReset, 0, 10_000_000UL, out uint resetCommand))
            {
                Console.WriteLine("  HCRST clear: TIMEOUT, USBCMD=" + Hex(resetCommand));
                return false;
            }

            Console.WriteLine("  HCRST clear: OK");

            if (!WaitRegisterBits(operationalBase + OperationalUsbStsOffset, UsbStsControllerNotReady, 0, 10_000_000UL, out uint readyStatus))
            {
                Console.WriteLine("  CNR clear: TIMEOUT, USBSTS=" + Hex(readyStatus));
                return false;
            }

            Console.WriteLine("  CNR clear: OK");

            uint usbCmdAfter = Read32(operationalBase + OperationalUsbCmdOffset);
            uint usbStsAfter = Read32(operationalBase + OperationalUsbStsOffset);
            uint pageSize = Read32(operationalBase + OperationalPageSizeOffset);
            uint config = Read32(operationalBase + OperationalConfigOffset);

            Console.WriteLine("  USBCMD after:  " + Hex(usbCmdAfter));
            Console.WriteLine("  USBSTS after:  " + Hex(usbStsAfter));
            Console.WriteLine("  PAGESIZE:      " + Hex(pageSize));
            Console.WriteLine("  CONFIG:        " + Hex(config));

            if ((usbStsAfter & UsbStsHostControllerError) != 0)
            {
                Console.WriteLine("  reset completed with HCE set.");
                return false;
            }

            if ((usbStsAfter & UsbStsHalted) == 0)
            {
                Console.WriteLine("  reset completed but controller is unexpectedly running.");
                return false;
            }

            if ((pageSize & 1U) == 0)
            {
                Console.WriteLine("  controller does not advertise 4 KiB pages; ring stage blocked.");
                return false;
            }

            Console.WriteLine();
            Console.WriteLine("RESULT: xHCI Stage 1 PASS - controller reset and ready for ring setup.");
            Console.WriteLine("Controller intentionally remains HALTED.");
            return true;
        }

        private static bool TryLocateController(out ulong xhciBase, out ulong operationalBase, out string detail)
        {
            xhciBase = 0;
            operationalBase = 0;
            detail = string.Empty;

            if (Limine.HHDM.Response == null)
            {
                detail = "Limine HHDM response missing";
                return false;
            }

            ulong hhdm = Limine.HHDM.Response->Offset;

            DeviceMapper.EnsureMapped(PcieRegPhysicalBase);
            if (!IsDeviceMapped(PcieRegPhysicalBase))
            {
                detail = "BCM2711 PCIe registers are not Device-mapped";
                return false;
            }

            ulong pcieRegs = PcieRegPhysicalBase + hhdm;
            byte secondaryBus = Read8(pcieRegs + PciSecondaryBusOffset);
            if (secondaryBus == 0)
            {
                detail = "secondary PCI bus is zero";
                return false;
            }

            uint bdf = (uint)secondaryBus << 20;
            Write32(pcieRegs + PcieExtCfgIndexOffset, bdf);
            Rpi4PageTableNative.DsbIsb();
            ulong vl805Config = pcieRegs + PcieExtCfgDataOffset;

            uint bar0Low = Read32(vl805Config + PciBar0Offset);
            if ((bar0Low & 1U) != 0)
            {
                detail = "VL805 BAR0 is I/O space";
                return false;
            }

            uint barType = (bar0Low >> 1) & 0x3U;
            if (barType == 0x3U)
            {
                detail = "VL805 BAR0 has reserved memory type";
                return false;
            }

            ulong pciBar = bar0Low & 0xFFFFFFF0U;
            if (barType == 0x2U)
                pciBar |= (ulong)Read32(vl805Config + PciBar0Offset + 4UL) << 32;

            if (pciBar < PcieBusMmioBase || pciBar >= PcieBusMmioBase + PcieBusMmioLength)
            {
                detail = "VL805 BAR0 outside Pi PCIe MMIO aperture: " + Hex(pciBar);
                return false;
            }

            ushort command = Read16(vl805Config + PciCommandOffset);
            if ((command & PciCommandMemorySpace) == 0)
            {
                Write16(vl805Config + PciCommandOffset, (ushort)(command | PciCommandMemorySpace));
                Rpi4PageTableNative.DsbIsb();
                command = Read16(vl805Config + PciCommandOffset);
                if ((command & PciCommandMemorySpace) == 0)
                {
                    detail = "cannot enable VL805 PCI Memory Space decoding";
                    return false;
                }
            }

            ulong xhciPhysical = PcieCpuMmioWindow + (pciBar - PcieBusMmioBase);
            DeviceMapper.EnsureMapped(xhciPhysical);
            if (!IsDeviceMapped(xhciPhysical))
            {
                detail = "xHCI BAR is not Device-mapped; read-only probe/fallback did not succeed";
                return false;
            }

            xhciBase = xhciPhysical + hhdm;
            uint capability0 = Read32(xhciBase);
            byte capLength = (byte)(capability0 & 0xFFU);
            ushort hciVersion = (ushort)(capability0 >> 16);
            if (capLength < 0x20 || capLength > 0x80 || hciVersion < 0x0090 || hciVersion > 0x0200)
            {
                detail = "invalid xHCI capability signature: CAP=" + Hex(capability0);
                xhciBase = 0;
                return false;
            }

            operationalBase = xhciBase + capLength;
            detail = "MMIO=" + Hex(xhciPhysical) + ", xHCI=" + Hex(hciVersion);
            return true;
        }

        private static bool TryTakeLegacyOwnership(ulong xhciBase, out string detail)
        {
            detail = "not present (UEFI handoff already clean)";

            uint hccParams1 = Read32(xhciBase + HccParams1Offset);
            uint extOffsetDwords = hccParams1 >> 16;
            if (extOffsetDwords == 0)
                return true;

            ulong current = xhciBase + ((ulong)extOffsetDwords << 2);
            for (int hop = 0; hop < 64; hop++)
            {
                uint header = Read32(current);
                byte id = (byte)(header & 0xFFU);
                byte next = (byte)((header >> 8) & 0xFFU);

                if (id == XhciExtCapLegacySupport)
                {
                    uint legsup = header;
                    bool biosOwned = (legsup & LegacyBiosOwned) != 0;
                    bool osOwned = (legsup & LegacyOsOwned) != 0;

                    if (!osOwned)
                    {
                        Write32(current, legsup | LegacyOsOwned);
                        Rpi4PageTableNative.DsbIsb();
                    }

                    if (biosOwned && !WaitRegisterBits(current, LegacyBiosOwned, 0, 1_000_000UL, out uint finalLegsup))
                    {
                        detail = "BIOS-owned semaphore did not clear, USBLEGSUP=" + Hex(finalLegsup);
                        return false;
                    }

                    uint after = Read32(current);
                    if ((after & LegacyOsOwned) == 0)
                    {
                        detail = "OS-owned semaphore did not stick, USBLEGSUP=" + Hex(after);
                        return false;
                    }

                    detail = "OK, USBLEGSUP=" + Hex(after);
                    return true;
                }

                if (next == 0)
                    return true;

                current += (ulong)next << 2;
            }

            detail = "extended capability chain exceeded 64 entries";
            return false;
        }

        private static bool WaitRegisterBits(ulong register, uint mask, uint wanted, ulong timeoutMicroseconds, out uint lastValue)
        {
            ulong frequency = Rpi4TimerNative.GetFrequency();
            if (frequency == 0)
            {
                lastValue = Read32(register);
                return (lastValue & mask) == wanted;
            }

            ulong start = Rpi4TimerNative.GetCounter();
            ulong delta = (frequency * timeoutMicroseconds + 999_999UL) / 1_000_000UL;
            ulong deadline = start + delta;

            do
            {
                lastValue = Read32(register);
                if ((lastValue & mask) == wanted)
                    return true;
            }
            while (Rpi4TimerNative.GetCounter() < deadline);

            lastValue = Read32(register);
            return (lastValue & mask) == wanted;
        }

        private static bool IsDeviceMapped(ulong physicalAddress)
        {
            if (Limine.HHDM.Response == null)
                return false;

            ulong hhdm = Limine.HHDM.Response->Offset;
            ulong mair = Rpi4PageTableNative.ReadMair();
            ulong ttbr1Physical = Rpi4PageTableNative.ReadTtbr1() & AddressMask;
            if (ttbr1Physical == 0)
                return false;

            ulong* l0 = (ulong*)(ttbr1Physical + hhdm);
            ulong l0Entry = l0[(physicalAddress >> 39) & 0x1FFUL];
            if ((l0Entry & DescriptorValid) == 0 || (l0Entry & DescriptorTable) == 0)
                return false;

            ulong* l1 = (ulong*)((l0Entry & AddressMask) + hhdm);
            ulong l1Entry = l1[(physicalAddress >> 30) & 0x1FFUL];
            if ((l1Entry & DescriptorValid) == 0)
                return false;

            ulong entry;
            if ((l1Entry & DescriptorTable) == 0)
            {
                entry = l1Entry;
            }
            else
            {
                ulong* l2 = (ulong*)((l1Entry & AddressMask) + hhdm);
                ulong l2Entry = l2[(physicalAddress >> 21) & 0x1FFUL];
                if ((l2Entry & DescriptorValid) == 0 || (l2Entry & DescriptorTable) != 0)
                    return false;
                entry = l2Entry;
            }

            int mairIndex = (int)((entry >> 2) & 0x7UL);
            byte attr = (byte)((mair >> (mairIndex * 8)) & 0xFFUL);
            return attr == 0x00 || attr == 0x04;
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

        private static string Hex(ushort value) => "0x" + value.ToString("X4");
        private static string Hex(uint value) => "0x" + value.ToString("X8");
        private static string Hex(ulong value) => "0x" + value.ToString("X");
    }

    internal static partial class Rpi4TimerNative
    {
        [LibraryImport("*", EntryPoint = "_native_arm64_timer_get_frequency")]
        [SuppressGCTransition]
        internal static partial ulong GetFrequency();

        [LibraryImport("*", EntryPoint = "_native_arm64_timer_get_counter")]
        [SuppressGCTransition]
        internal static partial ulong GetCounter();
    }
}
