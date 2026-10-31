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
            for (int i = 0; i < devices.Count; i++)
            {
                DeviceDescriptor d = devices[i];
                if (d == null || !IsAudioClass(d.ClassCode, d.Subclass))
                    continue;

                found++;
                Write("[AUDIO] " + d.Id.Address + " [" +
                    d.VendorId.ToString("X4") + ":" + d.DeviceId.ToString("X4") +
                    "] class=" + d.ClassCode.ToString("X2") + ":" +
                    d.Subclass.ToString("X2") + ":" + d.ProgrammingInterface.ToString("X2") +
                    " " + DescribeController(d.ClassCode, d.Subclass, d.ProgrammingInterface), shellOutput);
                Write("[AUDIO] driver=UNVERIFIED codec=UNKNOWN pcm=NOT_TESTED", shellOutput);
            }

            Write("[AUDIO] Discovered PCI audio functions: " + found, shellOutput);
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
            return "Legacy multimedia audio controller (no stock AC97 driver)";
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
