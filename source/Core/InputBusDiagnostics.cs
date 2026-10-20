using System;
using System.Collections.Generic;
using ZonderqOS.Hardware;
using ZonderqOS.Platform;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Read-only input-bus survey. PCI function enumeration reveals host
    /// controllers, never USB HID devices behind an xHCI/EHCI controller.
    /// Touchpad transport may be PS/2 or ACPI/I2C HID and cannot be inferred
    /// from the PCI function class alone.
    /// </summary>
    public static class InputBusDiagnostics
    {
        public static bool PrintReport(bool shellOutput = false)
        {
            Write("[INPUT] ===== REAL HARDWARE INPUT DIAGNOSTICS =====", shellOutput);
            Write("[INPUT] Scanning PCI/PCIe config space (read-only)...", shellOutput);

            List<DeviceDescriptor> devices;
            if (!PciSysfsProvider.TryDiscover(out devices) || devices == null)
            {
                Write("[INPUT] PCI scan UNAVAILABLE on this backend.", shellOutput);
                PrintLimitations(shellOutput);
                return false;
            }

            int usbHosts = 0;
            int serialBus = 0;
            int pciInputs = 0;
            int legacyBridges = 0;

            for (int i = 0; i < devices.Count; i++)
            {
                DeviceDescriptor d = devices[i];
                string label = null;
                if (d.ClassCode == 0x0C && d.Subclass == 0x03)
                {
                    label = "USB " + UsbControllerType(d.ProgrammingInterface) + " host";
                    usbHosts++;
                }
                else if (d.ClassCode == 0x0C && d.Subclass == 0x80)
                {
                    // Class 0C:80 is generic 'other serial bus', NOT proof of I2C.
                    label = "Other serial bus (possibly I2C; unverified)";
                    serialBus++;
                }
                else if (d.ClassCode == 0x0C && d.Subclass == 0x05)
                {
                    label = "SMBus controller (not proof of I2C HID)";
                    serialBus++;
                }
                else if (d.ClassCode == 0x09)
                {
                    label = "PCI input controller (device type unverified)";
                    pciInputs++;
                }
                else if (d.ClassCode == 0x06 && d.Subclass == 0x01)
                {
                    label = "ISA/LPC bridge (possible PS/2 route)";
                    legacyBridges++;
                }

                if (label != null)
                {
                    Write("[INPUT] PCI " + d.Id.Address +
                        " [" + d.VendorId.ToString("X4") + ":" + d.DeviceId.ToString("X4") +
                        "] class=" + d.ClassCode.ToString("X2") + ":" +
                        d.Subclass.ToString("X2") + ":" +
                        d.ProgrammingInterface.ToString("X2") + " " + label, shellOutput);
                }
            }

            Write("[INPUT] PCI functions scanned: " + devices.Count +
                "; USB hosts: " + usbHosts +
                "; serial bus: " + serialBus +
                "; PCI input: " + pciInputs +
                "; ISA/LPC: " + legacyBridges, shellOutput);
            if (usbHosts == 0)
                Write("[INPUT] No PCI USB host controller in this scan (not proof USB is absent).", shellOutput);

            PrintLimitations(shellOutput);
            return true;
        }

        private static string UsbControllerType(byte progIf)
        {
            switch (progIf)
            {
                case 0x00: return "UHCI";
                case 0x10: return "OHCI";
                case 0x20: return "EHCI";
                case 0x30: return "xHCI";
                case 0x80: return "generic";
                case 0xFE: return "device";
                default: return "unknown prog-if 0x" + progIf.ToString("X2");
            }
        }

        private static void PrintLimitations(bool shellOutput)
        {
            Write("[INPUT] USB mouse: NOT VERIFIED (Cosmos 3.0.85 has no USB HID mouse enumeration).", shellOutput);
            Write("[INPUT] Touchpad: NOT VERIFIED (PS/2 or ACPI/I2C HID; no HID identity scan).", shellOutput);
            Write("[INPUT] PCI USB host != connected USB mouse; PCI I2C candidate != touchpad.", shellOutput);
            Write("[INPUT] For PS/2 device init see early [PS2Controller]/[PS2Mouse] serial logs.", shellOutput);
            Write("[INPUT] After login: inputscan | lspci -v | dmesg", shellOutput);
            Write("[INPUT] ==========================================", shellOutput);
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
