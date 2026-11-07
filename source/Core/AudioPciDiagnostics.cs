using System;
using System.Collections.Generic;
using ZonderqOS.Hardware;
using ZonderqOS.Platform;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Read-only PCI multimedia inventory. Discovering a controller does not
    /// mean that an audio driver bound or that PCM reached a speaker.
    /// Never enables PCI bus mastering, BARs, interrupts or DMA.
    /// </summary>
    public static class AudioPciDiagnostics
    {
        public static bool PrintReport(bool shellOutput = true)
        {
            Write("[AUDIO] PCI audio inventory (configuration reads only)", shellOutput);

            List<DeviceDescriptor> devices;
            if (!PciSysfsProvider.TryDiscover(out devices) || devices == null)
            {
                Write("[AUDIO] PCI discovery unavailable; audio hardware is unverified.", shellOutput);
                return false;
            }

            int found = 0;
            int supportedInterfaceCandidates = 0;
            int unsupportedInterfaces = 0;
            for (int i = 0; i < devices.Count; i++)
            {
                DeviceDescriptor d = devices[i];
                if (d == null || !IsAudioClass(d.ClassCode, d.Subclass))
                    continue;

                found++;
                if (d.ProgrammingInterface == 0x00)
                    supportedInterfaceCandidates++;
                else
                    unsupportedInterfaces++;
                Write("[AUDIO] " + d.Id.Address + " [" +
                    d.VendorId.ToString("X4") + ":" + d.DeviceId.ToString("X4") +
                    "] class=" + d.ClassCode.ToString("X2") + ":" +
                    d.Subclass.ToString("X2") + ":" + d.ProgrammingInterface.ToString("X2") +
                    " " + DescribeController(d.ClassCode, d.Subclass, d.ProgrammingInterface), shellOutput);
                Write("[AUDIO] pci-identity=" +
                    DescribePciIdentity(d.VendorId, d.DeviceId, d.ClassCode, d.Subclass, d.ProgrammingInterface), shellOutput);
                Write("[AUDIO] driver=UNVERIFIED codec=UNKNOWN pcm=NOT_TESTED", shellOutput);
            }

            Write("[AUDIO] Discovered PCI audio functions: " + found, shellOutput);
            Write("[AUDIO] Standard-interface candidates: " + supportedInterfaceCandidates +
                "; unsupported programming interfaces: " + unsupportedInterfaces, shellOutput);
            if (found == 0)
                Write("[AUDIO] No PCI audio function discovered; USB/other audio is not excluded.", shellOutput);
            Write("[AUDIO] Inventory is not proof of a bound output or audible sound.", shellOutput);
            return true;
        }

        public static bool IsAudioClass(byte classCode, byte subclass)
        {
            // PCI 04:01 = legacy multimedia audio; 04:03 = HD Audio.
            // 04:00 is a video controller, not a sound card.
            return classCode == 0x04 && (subclass == 0x01 || subclass == 0x03);
        }

        public static string DescribeController(byte classCode, byte subclass, byte programmingInterface)
        {
            if (!IsAudioClass(classCode, subclass))
                return "not a PCI audio function";
            if (subclass == 0x03 && programmingInterface == 0x00)
                return "HD Audio controller (stock Cosmos HdAudioDriver candidate)";
            if (subclass == 0x03)
                return "HD Audio subclass, unsupported programming interface";
            if (subclass == 0x01 && programmingInterface != 0x00)
                return "Legacy multimedia audio subclass, unsupported programming interface";
            return "Legacy multimedia audio controller (AC97 not verified; no stock AC97 driver)";
        }

        /// <summary>
        /// Read-only identification of the two Intel PCI identities used by
        /// QEMU's HDA and AC'97 controllers. Vendor/device IDs do not prove
        /// a bound driver, working DMA/IRQ, codec, or speaker output.
        /// Other devices remain explicitly unidentified.
        /// </summary>
        public static string DescribePciIdentity(
            ushort vendorId, ushort deviceId, byte classCode, byte subclass, byte programmingInterface = 0)
        {
            if (!IsAudioClass(classCode, subclass))
                return "NOT_AUDIO";
            if (vendorId == 0x8086 && deviceId == 0x2668 && subclass == 0x03 && programmingInterface == 0x00)
                return "Intel ICH6 HDA (QEMU intel-hda candidate; driver unverified)";
            if (vendorId == 0x8086 && deviceId == 0x2415 && subclass == 0x01 && programmingInterface == 0x00)
                return "Intel ICH AC97 (QEMU AC97 candidate; no stock AC97 driver)";
            return "UNKNOWN_AUDIO_IDENTITY (class-only identification)";
        }

        private static void Write(string message, bool shellOutput)
        {
            if (shellOutput)
                CommandIO.WriteLine(message);
            else
                Console.WriteLine(message);
        }
    }
}
