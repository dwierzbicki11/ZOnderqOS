using System;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.Core.ARM64.Cpu;

namespace ZonderqOS
{
    /// <summary>
    /// Thin managed wrapper around the native AArch64 C VL805 reset backend.
    /// Critical MMIO, mailbox, cache maintenance, ownership and HCRST sequencing
    /// live in source/Native/arm64/rpi4_xhci_native.c and are linked directly by
    /// Cosmos.Build.CC + NativeAOT DirectPInvoke.
    /// </summary>
    internal static unsafe class Rpi4XhciResetStage
    {
        private const ulong PcieRegPhysicalBase = 0xFD500000UL;
        private const ulong MailboxPhysicalBase = 0xFE00B880UL;
        private const ulong PcieCpuMmioWindow = 0x600000000UL;

        private const int ResultWords = 32;
        private const int RMagic = 0;
        private const int RStage = 1;
        private const int RError = 2;
        private const int RRetry = 3;
        private const int RMailboxBufferPhysical = 4;
        private const int RMailboxMessage = 5;
        private const int RMailboxReply = 6;
        private const int RMailboxStatus = 7;
        private const int RMailboxTagStatus = 8;
        private const int RSecondaryBus = 9;
        private const int RXhciPhysical = 10;
        private const int RCapability0 = 11;
        private const int RPciCommandBefore = 12;
        private const int RPciCommandAfter = 13;
        private const int RLegsupBefore = 14;
        private const int RLegsupAfter = 15;
        private const int RUsbCmdBefore = 16;
        private const int RUsbStsBefore = 17;
        private const int RUsbCmdAfter = 18;
        private const int RUsbStsAfter = 19;
        private const int RPageSize = 20;
        private const int RConfig = 21;
        private const int RLastUsbCmd = 22;
        private const int RLastUsbSts = 23;
        private const int RMailboxAttempts = 24;
        private const int RMailboxBufferVirtual = 25;
        private const int RCacheLine = 26;
        private const int RMailboxAllocationBytes = 27;

        public static bool Run()
        {
            Console.WriteLine("RPi4 xHCI Stage 1D - low-memory mailbox + native C backend:");
            Console.WriteLine("  backend:         AArch64 C / clang / DirectPInvoke");
            Console.WriteLine("  mailbox buffer:  Cosmos heap, cache-line isolated");
            Console.WriteLine("  DMA/rings:       OFF");
            Console.WriteLine("  USB enumeration: OFF");
            Console.WriteLine("  HID keyboard:    OFF");
            Console.WriteLine();

            if (Limine.HHDM.Response == null)
            {
                Console.WriteLine("  native stage: FAILED - Limine HHDM response missing");
                return false;
            }

            // The native backend deliberately owns the register accesses, but page-table
            // construction remains with Cosmos. The read-only probe executed immediately
            // before this stage also installs/verifies the high PCIe aperture mapping.
            DeviceMapper.EnsureMapped(PcieRegPhysicalBase);
            DeviceMapper.EnsureMapped(MailboxPhysicalBase);
            DeviceMapper.EnsureMapped(PcieCpuMmioWindow);

            ulong* result = stackalloc ulong[ResultWords];
            for (int i = 0; i < ResultWords; i++)
                result[i] = 0;

            int nativeCode = Rpi4XhciNative.Run(Limine.HHDM.Response->Offset, result);

            Console.WriteLine("  native magic:      " + Hex(result[RMagic]));
            Console.WriteLine("  stage reached:     " + result[RStage]);
            Console.WriteLine("  secondary bus:     " + result[RSecondaryBus]);
            Console.WriteLine("  xHCI physical:     " + Hex(result[RXhciPhysical]));
            Console.WriteLine("  CAP DWORD0:        " + Hex32(result[RCapability0]));
            Console.WriteLine("  PCI CMD before:    " + Hex16(result[RPciCommandBefore]));
            Console.WriteLine("  PCI CMD after:     " + Hex16(result[RPciCommandAfter]));
            Console.WriteLine();

            if (result[RMailboxAttempts] != 0)
            {
                Console.WriteLine("  firmware mailbox:");
                Console.WriteLine("    attempts:        " + result[RMailboxAttempts]);
                Console.WriteLine("    buffer virtual:  " + Hex(result[RMailboxBufferVirtual]));
                Console.WriteLine("    buffer physical: " + Hex(result[RMailboxBufferPhysical]));
                Console.WriteLine("    cache line:      " + result[RCacheLine] + " bytes");
                Console.WriteLine("    heap allocation: " + result[RMailboxAllocationBytes] + " bytes");
                Console.WriteLine("    request word:    " + Hex32(result[RMailboxMessage]));
                Console.WriteLine("    reply word:      " + Hex32(result[RMailboxReply]));
                Console.WriteLine("    status:          " + Hex32(result[RMailboxStatus]));
                Console.WriteLine("    tag status:      " + Hex32(result[RMailboxTagStatus]));
                Console.WriteLine();
            }

            if (result[RLegsupBefore] != 0 || result[RLegsupAfter] != 0)
            {
                Console.WriteLine("  legacy ownership:");
                Console.WriteLine("    USBLEGSUP before: " + Hex32(result[RLegsupBefore]));
                Console.WriteLine("    USBLEGSUP after:  " + Hex32(result[RLegsupAfter]));
                Console.WriteLine();
            }

            Console.WriteLine("  xHCI reset registers:");
            Console.WriteLine("    USBCMD before: " + Hex32(result[RUsbCmdBefore]));
            Console.WriteLine("    USBSTS before: " + Hex32(result[RUsbStsBefore]));
            Console.WriteLine("    USBCMD after:  " + Hex32(result[RUsbCmdAfter]));
            Console.WriteLine("    USBSTS after:  " + Hex32(result[RUsbStsAfter]));
            Console.WriteLine("    last USBCMD:   " + Hex32(result[RLastUsbCmd]));
            Console.WriteLine("    last USBSTS:   " + Hex32(result[RLastUsbSts]));
            Console.WriteLine("    PAGESIZE:      " + Hex32(result[RPageSize]));
            Console.WriteLine("    CONFIG:        " + Hex32(result[RConfig]));
            Console.WriteLine("    recovery retry: " + (result[RRetry] != 0 ? "YES" : "NO"));
            Console.WriteLine();

            if (nativeCode != 0)
            {
                long storedError = unchecked((long)result[RError]);
                Console.WriteLine("RESULT: xHCI Stage 1D FAILED");
                Console.WriteLine("  native rc: " + nativeCode);
                Console.WriteLine("  stored rc: " + storedError);
                Console.WriteLine("  meaning:   " + ErrorText(nativeCode));
                Console.WriteLine("Rings/DMA/HID were NOT attempted.");
                return false;
            }

            Console.WriteLine("RESULT: xHCI Stage 1D PASS - firmware handoff + HCRST completed.");
            Console.WriteLine("Controller intentionally remains HALTED; rings/DMA are still OFF.");
            return true;
        }

        private static string ErrorText(int code)
        {
            return code switch
            {
                -1 => "bad native argument",
                -2 => "BCM2711 secondary PCI bus is zero",
                -3 => "VL805 PCI function/class/config is not usable",
                -4 => "VL805 BAR0 is invalid or outside the Pi PCIe aperture",
                -5 => "xHCI capability signature is invalid",
                -6 => "Cosmos heap mailbox buffer VA->PA translation failed",
                -7 => "Cosmos heap mailbox buffer is still outside the <1 GiB VideoCore DMA window",
                -8 => "mailbox TX stayed full",
                -9 => "mailbox firmware response timed out",
                -10 => "firmware property request returned failure",
                -11 => "xHCI legacy ownership timed out",
                -12 => "xHCI OS-owned semaphore did not stick",
                -13 => "controller reported HCE before reset",
                -14 => "controller failed to halt",
                -15 => "HCRST stayed asserted after 10 seconds",
                -16 => "Controller Not Ready stayed asserted after reset",
                -17 => "controller reported HCE after reset",
                -18 => "controller unexpectedly ran after reset",
                -19 => "controller does not advertise 4 KiB pages",
                -20 => "could not allocate a safe cache-line-isolated mailbox buffer",
                _ => "unknown native failure"
            };
        }

        private static string Hex16(ulong value) => "0x" + ((ushort)value).ToString("X4");
        private static string Hex32(ulong value) => "0x" + ((uint)value).ToString("X8");
        private static string Hex(ulong value) => "0x" + value.ToString("X");
    }

    internal static unsafe partial class Rpi4XhciNative
    {
        [LibraryImport("zq_rpi4_native", EntryPoint = "zq_rpi4_xhci_stage1_native")]
        internal static partial int Run(ulong hhdmOffset, ulong* resultWords);
    }
}
