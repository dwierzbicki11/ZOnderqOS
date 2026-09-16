using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.ARM64.Cpu;

namespace ZonderqOS
{
    /// <summary>
    /// Raspberry Pi 4 VL805/xHCI reset stage.
    ///
    /// Stage 1B adds the Raspberry Pi firmware mailbox handoff used by Linux for
    /// the onboard VL805 before issuing the normal xHCI HCRST sequence. No DMA,
    /// command/event rings, USB enumeration or HID traffic is enabled here.
    /// </summary>
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

        // Raspberry Pi firmware property mailbox. The DT mailbox address is
        // 0x7E00B880 on the VC bus; BCM2711 maps that peripheral window at
        // 0xFE00B880 for the ARM CPU.
        private const ulong MailboxPhysicalBase = 0xFE00B880UL;
        private const ulong Mailbox0ReadOffset = 0x00UL;
        private const ulong Mailbox0StatusOffset = 0x18UL;
        private const ulong Mailbox1WriteOffset = 0x20UL;
        private const ulong Mailbox1StatusOffset = 0x38UL;
        private const uint MailboxStatusFull = 1U << 31;
        private const uint MailboxStatusEmpty = 1U << 30;
        private const uint MailboxPropertyChannel = 8U;

        private const uint FirmwareStatusRequest = 0x00000000U;
        private const uint FirmwareStatusSuccess = 0x80000000U;
        private const uint FirmwareNotifyXhciResetTag = 0x00030058U;
        private const uint FirmwareVl805PciAddress = 0x00100000U;
        private const uint FirmwarePropertyEnd = 0U;

        // One property tag with a single u32 payload:
        // message header (8) + tag header/payload (16) + end tag (4) = 28 bytes.
        private const uint FirmwareMessageBytes = 28U;
        private const int FirmwareBufferStorageBytes = 64;
        private const ulong GpuUncachedAlias = 0xC0000000UL;
        private const ulong GpuAliasPhysicalLimit = 0x40000000UL;

        private const ulong AddressMask = 0x0000FFFFFFFFF000UL;
        private const ulong DescriptorValid = 1UL << 0;
        private const ulong DescriptorTable = 1UL << 1;

        public static bool Run()
        {
            Console.WriteLine("RPi4 xHCI Stage 1B - firmware handoff + controller reset:");
            Console.WriteLine("  DMA/rings:       OFF");
            Console.WriteLine("  USB enumeration: OFF");
            Console.WriteLine("  HID keyboard:    OFF");
            Console.WriteLine();

            if (!TryLocateController(out ulong xhciBase, out ulong operationalBase, out string locateDetail))
            {
                Console.WriteLine("  pre-firmware controller locate: FAILED");
                Console.WriteLine("  detail: " + locateDetail);
                return false;
            }

            Console.WriteLine("  pre-firmware controller locate: OK - " + locateDetail);

            Console.WriteLine("  notifying Raspberry Pi firmware about VL805 reset...");
            if (!TryFirmwareNotifyXhciReset(out string firmwareDetail))
            {
                Console.WriteLine("  firmware xHCI reset notify: FAILED");
                Console.WriteLine("  detail: " + firmwareDetail);
                return false;
            }

            Console.WriteLine("  firmware xHCI reset notify: OK - " + firmwareDetail);

            // Linux waits 200..1000 us after the firmware reset notification so
            // the VL805 firmware has time to start. Use the conservative end.
            DelayMicroseconds(1_000UL);

            // The firmware operation can alter the endpoint state. Re-discover the
            // BAR and re-enable Memory Space decoding instead of trusting stale state.
            if (!TryLocateController(out xhciBase, out operationalBase, out string postFirmwareLocateDetail))
            {
                Console.WriteLine("  post-firmware controller locate: FAILED");
                Console.WriteLine("  detail: " + postFirmwareLocateDetail);
                return false;
            }

            Console.WriteLine("  post-firmware controller locate: OK - " + postFirmwareLocateDetail);

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
                Console.WriteLine("  controller already reports HCE; refusing HCRST.");
                return false;
            }

            Console.WriteLine("  halting controller...");
            if ((usbStsBefore & UsbStsHalted) == 0)
            {
                Write32(operationalBase + OperationalUsbCmdOffset, usbCmdBefore & ~UsbCmdRun);
                Rpi4PageTableNative.DsbIsb();

                if (!WaitRegisterBits(
                        operationalBase + OperationalUsbStsOffset,
                        UsbStsHalted,
                        UsbStsHalted,
                        1_000_000UL,
                        out uint haltStatus))
                {
                    Console.WriteLine("  controller halt: TIMEOUT, USBSTS=" + Hex(haltStatus));
                    return false;
                }
            }

            Console.WriteLine("  controller halt: OK");

            uint haltedCmd = Read32(operationalBase + OperationalUsbCmdOffset);
            Console.WriteLine("  issuing HCRST...");
            Write32(
                operationalBase + OperationalUsbCmdOffset,
                (haltedCmd & ~UsbCmdRun) | UsbCmdHostControllerReset);
            Rpi4PageTableNative.DsbIsb();

            if (!WaitRegisterBits(
                    operationalBase + OperationalUsbCmdOffset,
                    UsbCmdHostControllerReset,
                    0,
                    10_000_000UL,
                    out uint resetCommand))
            {
                uint timeoutStatus = Read32(operationalBase + OperationalUsbStsOffset);
                Console.WriteLine("  HCRST clear: TIMEOUT, USBCMD=" + Hex(resetCommand));
                Console.WriteLine("  USBSTS at timeout: " + Hex(timeoutStatus));
                return false;
            }

            Console.WriteLine("  HCRST clear: OK");

            if (!WaitRegisterBits(
                    operationalBase + OperationalUsbStsOffset,
                    UsbStsControllerNotReady,
                    0,
                    10_000_000UL,
                    out uint readyStatus))
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
            Console.WriteLine("RESULT: xHCI Stage 1B PASS - firmware handoff + HCRST completed.");
            Console.WriteLine("Controller intentionally remains HALTED; rings/DMA are still OFF.");
            return true;
        }

        private static bool TryFirmwareNotifyXhciReset(out string detail)
        {
            detail = string.Empty;

            if (Limine.HHDM.Response == null)
            {
                detail = "Limine HHDM response missing";
                return false;
            }

            DeviceMapper.EnsureMapped(MailboxPhysicalBase);
            if (!IsDeviceMapped(MailboxPhysicalBase))
            {
                detail = "BCM2711 mailbox MMIO is not Device-mapped";
                return false;
            }

            ulong mailbox = MailboxPhysicalBase + Limine.HHDM.Response->Offset;

            byte* storage = stackalloc byte[FirmwareBufferStorageBytes];
            ulong rawAddress = (ulong)storage;
            ulong alignedAddress = (rawAddress + 15UL) & ~15UL;
            uint* buffer = (uint*)alignedAddress;

            buffer[0] = FirmwareMessageBytes;
            buffer[1] = FirmwareStatusRequest;
            buffer[2] = FirmwareNotifyXhciResetTag;
            buffer[3] = sizeof(uint);
            buffer[4] = 0; // request size; firmware sets response flag/size
            buffer[5] = FirmwareVl805PciAddress;
            buffer[6] = FirmwarePropertyEnd;

            if (!TryGetContiguousPhysicalRange(
                    alignedAddress,
                    FirmwareMessageBytes,
                    out ulong physicalAddress,
                    out string physicalDetail))
            {
                detail = "mailbox property buffer translation failed: " + physicalDetail;
                return false;
            }

            if ((physicalAddress & 0xFUL) != 0)
            {
                detail = "mailbox property buffer physical address is not 16-byte aligned: " + Hex(physicalAddress);
                return false;
            }

            if (physicalAddress >= GpuAliasPhysicalLimit ||
                physicalAddress + FirmwareMessageBytes > GpuAliasPhysicalLimit)
            {
                detail = "mailbox property buffer is above the VC 1-GiB alias window: " + Hex(physicalAddress);
                return false;
            }

            uint busAddress = (uint)(physicalAddress | GpuUncachedAlias);
            uint requestWord = busAddress | MailboxPropertyChannel;

            // Firmware reads this buffer through the VideoCore bus. The Pi 4 path
            // is not treated as cache-coherent here, so push CPU writes before the
            // mailbox request and invalidate again after the firmware response.
            CleanInvalidateRange(alignedAddress, FirmwareMessageBytes);
            Rpi4PageTableNative.DsbIsb();

            if (!WaitMailbox1Writable(mailbox, 1_000_000UL, out uint writeStatus))
            {
                detail = "MAIL1 remained full, status=" + Hex(writeStatus);
                return false;
            }

            Write32(mailbox + Mailbox1WriteOffset, requestWord);
            Rpi4PageTableNative.DsbIsb();

            if (!WaitMailbox0Response(
                    mailbox,
                    busAddress,
                    1_000_000UL,
                    out uint replyWord,
                    out uint ignoredReplies))
            {
                detail = "firmware mailbox reply timeout; last=" + Hex(replyWord) +
                         ", ignored=" + ignoredReplies;
                return false;
            }

            Rpi4PageTableNative.DsbIsb();
            CleanInvalidateRange(alignedAddress, FirmwareMessageBytes);
            Rpi4PageTableNative.DsbIsb();

            uint responseStatus = buffer[1];
            uint tagResponse = buffer[4];
            if (responseStatus != FirmwareStatusSuccess)
            {
                detail = "property request status=" + Hex(responseStatus) +
                         ", tag response=" + Hex(tagResponse);
                return false;
            }

            detail = "bus=" + Hex(busAddress) +
                     ", reply=" + Hex(replyWord) +
                     ", tag response=" + Hex(tagResponse) +
                     (ignoredReplies == 0 ? string.Empty : ", ignored=" + ignoredReplies);
            return true;
        }

        private static bool WaitMailbox1Writable(
            ulong mailbox,
            ulong timeoutMicroseconds,
            out uint lastStatus)
        {
            ulong frequency = Rpi4TimerNative.GetFrequency();
            if (frequency == 0)
            {
                for (int i = 0; i < 1_000_000; i++)
                {
                    lastStatus = Read32(mailbox + Mailbox1StatusOffset);
                    if ((lastStatus & MailboxStatusFull) == 0)
                        return true;
                }

                lastStatus = Read32(mailbox + Mailbox1StatusOffset);
                return false;
            }

            ulong start = Rpi4TimerNative.GetCounter();
            ulong delta = MicrosecondsToTicks(frequency, timeoutMicroseconds);
            do
            {
                lastStatus = Read32(mailbox + Mailbox1StatusOffset);
                if ((lastStatus & MailboxStatusFull) == 0)
                    return true;
            }
            while (Rpi4TimerNative.GetCounter() - start < delta);

            lastStatus = Read32(mailbox + Mailbox1StatusOffset);
            return (lastStatus & MailboxStatusFull) == 0;
        }

        private static bool WaitMailbox0Response(
            ulong mailbox,
            uint expectedBusAddress,
            ulong timeoutMicroseconds,
            out uint lastReply,
            out uint ignoredReplies)
        {
            lastReply = 0;
            ignoredReplies = 0;

            ulong frequency = Rpi4TimerNative.GetFrequency();
            ulong start = frequency == 0 ? 0 : Rpi4TimerNative.GetCounter();
            ulong delta = frequency == 0 ? 0 : MicrosecondsToTicks(frequency, timeoutMicroseconds);
            int fallbackSpins = 0;

            while (frequency != 0
                ? Rpi4TimerNative.GetCounter() - start < delta
                : fallbackSpins++ < 2_000_000)
            {
                uint status = Read32(mailbox + Mailbox0StatusOffset);
                if ((status & MailboxStatusEmpty) != 0)
                    continue;

                uint reply = Read32(mailbox + Mailbox0ReadOffset);
                lastReply = reply;

                uint channel = reply & 0xFU;
                uint data = reply & ~0xFU;
                if (channel == MailboxPropertyChannel && data == expectedBusAddress)
                    return true;

                ignoredReplies++;
            }

            return false;
        }

        private static bool TryGetContiguousPhysicalRange(
            ulong virtualAddress,
            uint length,
            out ulong physicalAddress,
            out string detail)
        {
            physicalAddress = 0;
            detail = string.Empty;

            if (length == 0)
            {
                detail = "zero-length range";
                return false;
            }

            ulong endVirtualAddress = virtualAddress + length - 1UL;
            ulong startPage = Rpi4PageTableNative.VirtToPhys(virtualAddress);
            ulong endPage = Rpi4PageTableNative.VirtToPhys(endVirtualAddress);
            if (startPage == 0 || endPage == 0)
            {
                detail = "VA->PA translation fault";
                return false;
            }

            ulong startPhysical = startPage + (virtualAddress & 0xFFFUL);
            ulong endPhysical = endPage + (endVirtualAddress & 0xFFFUL);
            if (endPhysical != startPhysical + length - 1UL)
            {
                detail = "buffer is not physically contiguous";
                return false;
            }

            physicalAddress = startPhysical;
            detail = "PA=" + Hex(startPhysical);
            return true;
        }

        private static void CleanInvalidateRange(ulong virtualAddress, uint length)
        {
            // The exported Cosmos helper operates on one cache line. Touch every
            // eight bytes so every possible line intersecting this tiny 28-byte
            // mailbox packet is covered without assuming a cache-line size here.
            for (ulong offset = 0; offset < length; offset += 8UL)
                Rpi4PageTableNative.CleanInvalidateDataCacheLine(virtualAddress + offset);

            Rpi4PageTableNative.CleanInvalidateDataCacheLine(virtualAddress + length - 1UL);
        }

        private static void DelayMicroseconds(ulong microseconds)
        {
            ulong frequency = Rpi4TimerNative.GetFrequency();
            if (frequency == 0)
                return;

            ulong delta = MicrosecondsToTicks(frequency, microseconds);
            ulong start = Rpi4TimerNative.GetCounter();
            while (Rpi4TimerNative.GetCounter() - start < delta)
            {
            }
        }

        private static ulong MicrosecondsToTicks(ulong frequency, ulong microseconds)
        {
            return (frequency * microseconds + 999_999UL) / 1_000_000UL;
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
            detail = "MMIO=" + Hex(xhciPhysical) + ", xHCI=" + Hex(hciVersion) + ", bus=" + secondaryBus;
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
            ulong delta = MicrosecondsToTicks(frequency, timeoutMicroseconds);

            do
            {
                lastValue = Read32(register);
                if ((lastValue & mask) == wanted)
                    return true;
            }
            while (Rpi4TimerNative.GetCounter() - start < delta);

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

    internal static partial class Rpi4PageTableNative
    {
        [LibraryImport("*", EntryPoint = "_native_arm64_va_to_pa")]
        [SuppressGCTransition]
        internal static partial ulong VirtToPhys(ulong virtualAddress);

        [LibraryImport("*", EntryPoint = "_native_arm64_dc_civac")]
        [SuppressGCTransition]
        internal static partial void CleanInvalidateDataCacheLine(ulong virtualAddress);
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
