using System;
using System.Collections.Generic;
using ZonderqOS.Hardware;
using ZonderqOS.Platform;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Read-only PCI NIC inventory for Cosmos Gen3 3.0.95.
    /// It is a hardware survey, NOT a network driver or link test.
    /// Does not touch the NIC's MMIO, interrupts, DMA, MAC registers or DHCP.
    /// </summary>
    public static class NetworkPciDiagnostics
    {
        public static bool PrintReport(bool shellOutput = true)
        {
            Write("[NIC3094] PCI network-controller inventory (read-only)", shellOutput);
            List<DeviceDescriptor> devices;
            if (!PciSysfsProvider.TryDiscover(out devices) || devices == null)
            {
                Write("[NIC3094] PCI discovery unavailable; no network adapters verified.", shellOutput);
                Write("[NIC3094] Unsupported on ARM64 until the PCI ECAM backend is implemented.", shellOutput);
                return false;
            }

            int found = 0;
            for (int i = 0; i < devices.Count; i++)
            {
                DeviceDescriptor d = devices[i];
                if (d == null || d.ClassCode != 0x02)
                    continue;

                found++;
                Write("[NIC3094] " + d.Id.Address + " [" +
                    d.VendorId.ToString("X4") + ":" + d.DeviceId.ToString("X4") +
                    "] class=" + d.ClassCode.ToString("X2") + ":" +
                    d.Subclass.ToString("X2") +
                    " prog_if=" + d.ProgrammingInterface.ToString("X2") +
                    " rev=" + (d.RevisionId == 0xFF ? "UNKNOWN" : d.RevisionId.ToString("X2")) +
                    " subsystem=" + (d.SubsystemVendorId == 0xFFFF || d.SubsystemDeviceId == 0xFFFF
                        ? "UNKNOWN" : d.SubsystemVendorId.ToString("X4") + ":" + d.SubsystemDeviceId.ToString("X4")) +
                    " " + DescribeDevice(d.VendorId, d.DeviceId, d.Subclass), shellOutput);
                if (d.VendorId == 0x10EC && d.DeviceId == 0x8136)
                {
                    PciNicConfigState state;
                    if (PciSysfsProvider.TryReadRealtekPciState(d, out state))
                        Write("[NIC3094] PCI_CMD=0x" + state.Command.ToString("X4") +
                            " PCI_STATUS=0x" + state.Status.ToString("X4") +
                            " io=" + (state.IoEnabled ? "on" : "off") +
                            " mem=" + (state.MemoryEnabled ? "on" : "off") +
                            " busmaster=" + (state.BusMasterEnabled ? "on" : "off") +
                            " legacy_irq_disabled=" + (state.LegacyInterruptDisabled ? "yes" : "no") +
                            " irq_line=" + (state.IrqLine == 0xFF ? "UNKNOWN" : state.IrqLine.ToString()) +
                            " irq_pin=" + state.IrqPin + " (read-only; IRQ not verified)", shellOutput);
                    List<PciCapabilityDescriptor> capabilities;
                    if (PciSysfsProvider.TryReadRealtekCapabilities(d, out capabilities))
                    {
                        if (capabilities.Count == 0)
                            Write("[NIC3094] PCI_CAPS=NONE (read-only; no MSI IRQ proof)", shellOutput);
                        for (int capIndex = 0; capIndex < capabilities.Count; capIndex++)
                        {
                            PciCapabilityDescriptor cap = capabilities[capIndex];
                            string name = cap.Id == 0x01 ? "PM" : cap.Id == 0x05 ? "MSI" :
                                cap.Id == 0x10 ? "PCIe" : cap.Id == 0x11 ? "MSI-X" : "OTHER";
                            Write("[NIC3094] PCI_CAP=0x" + cap.Id.ToString("X2") +
                                " " + name + " at=0x" + cap.Offset.ToString("X2") +
                                " (present only; IRQ not verified)", shellOutput);
                        }
                    }
                    else
                        Write("[NIC3094] PCI_CAPS=INVALID_OR_UNAVAILABLE (no IRQ proof)", shellOutput);
                    PciNicPowerState powerInfo;
                    if (PciSysfsProvider.TryReadRealtekPowerState(d, out powerInfo))
                    {
                        string powerName = !powerInfo.PmPresent ? "UNKNOWN_NO_PM_CAP" :
                            powerInfo.State == 0 ? "D0" : powerInfo.State == 1 ? "D1" :
                            powerInfo.State == 2 ? "D2" : "D3hot";
                        Write("[NIC3094] PCI_POWER=" + powerName +
                            " pme_enable=" + (powerInfo.PmeEnabled ? "1" : "0") +
                            " pme_status=" + (powerInfo.PmeStatus ? "1" : "0") +
                            " (read-only; no power transition)", shellOutput);
                    }
                    else
                        Write("[NIC3094] PCI_POWER=INVALID_OR_UNAVAILABLE", shellOutput);
                    PciNicInterruptCapabilities irqInfo;
                    if (PciSysfsProvider.TryReadRealtekInterruptCapabilities(d, out irqInfo))
                    {
                        Write("[NIC3094] MSI=" + (irqInfo.MsiPresent ? "present" : "absent") +
                            " enabled_bit=" + (irqInfo.MsiEnabled ? "1" : "0") +
                            " addr64=" + (irqInfo.Msi64Bit ? "yes" : "no") +
                            " (read-only; IRQ not verified)", shellOutput);
                        if (irqInfo.MsiXPresent)
                        {
                            Write("[NIC3094] MSI-X=present enabled_bit=" + (irqInfo.MsiXEnabled ? "1" : "0") +
                                " function_mask=" + (irqInfo.MsiXFunctionMasked ? "1" : "0") +
                                " vectors=" + irqInfo.MsiXVectors +
                                " table=BAR" + irqInfo.TableBir + "+0x" + irqInfo.TableOffset.ToString("X") +
                                " pba=BAR" + irqInfo.PbaBir + "+0x" + irqInfo.PbaOffset.ToString("X") +
                                " (unmapped; IRQ not verified)", shellOutput);
                            PciNicMsiXBarResolution msixBars;
                            if (PciSysfsProvider.TryResolveRealtekMsiXMemoryBars(d, out msixBars))
                                Write("[NIC3094] MSI-X_MEMORY_BARS=VALID table_phys=0x" +
                                    msixBars.TableAddress.ToString("X") +
                                    " table_bytes=" + msixBars.TableBytes +
                                    " pba_phys=0x" + msixBars.PbaAddress.ToString("X") +
                                    " pba_bytes=" + msixBars.PbaBytes +
                                    " (PCI metadata only; BAR aperture/mapping/IRQ unverified)", shellOutput);
                            else
                                Write("[NIC3094] MSI-X_MEMORY_BARS=INVALID_OR_UNASSIGNED" +
                                    " (no MMIO access attempted)", shellOutput);
                        }
                        else
                            Write("[NIC3094] MSI-X=absent (IRQ not verified)", shellOutput);
                    }
                    else
                        Write("[NIC3094] PCI_IRQ_CAPS=INVALID_OR_UNAVAILABLE (IRQ not verified)", shellOutput);
                    List<PciBarDescriptor> bars;
                    if (PciSysfsProvider.TryReadRealtekBars(d, out bars))
                    {
                        for (int barIndex = 0; barIndex < bars.Count; barIndex++)
                        {
                            PciBarDescriptor bar = bars[barIndex];
                            string kind = bar.Kind == PciBarKind.Io ? "IO" :
                                (bar.Kind == PciBarKind.Memory64 ? "MMIO64" : "MMIO32");
                            Write("[NIC3094] BAR" + bar.Index + "=" + kind + " base=0x" +
                                bar.Address.ToString("X") + " prefetch=" +
                                (bar.Prefetchable ? "yes" : "no") + " size=UNKNOWN", shellOutput);
                        }
                        if (bars.Count == 0)
                            Write("[NIC3094] No assigned RTL810xE BARs found", shellOutput);
                    }
                    else
                        Write("[NIC3094] RTL810xE BAR read unavailable or invalid", shellOutput);
                }
                Write("[NIC3094] driver=UNVERIFIED link=UNKNOWN mac=UNKNOWN ip=UNASSIGNED", shellOutput);
            }

            Write("[NIC3094] Discovered PCI network functions: " + found, shellOutput);
            if (found == 0)
                Write("[NIC3094] No PCI network controller discovered (not proof no network hardware exists).", shellOutput);

            Write("[NIC3094] Network driver and DHCP are disabled in this production image.", shellOutput);
            Write("[NIC3094] PCI discovery does not prove a working Ethernet link or Wi-Fi association.", shellOutput);
            return true;
        }

        public static string DescribeDevice(ushort vendor, ushort device, byte subclass)
        {
            if (vendor == 0x10EC && device == 0x8136)
                return "Realtek RTL810xE PCIe Fast Ethernet (driver not loaded)";
            if (vendor == 0x10EC && device == 0xB723)
                return "Realtek RTL8723BE Wi-Fi PCIe (driver not loaded)";
            if (vendor == 0x8086 && device == 0x100E)
                return "Intel 82540EM E1000 Ethernet (QEMU; driver not loaded)";
            if (vendor == 0x8086 && device == 0x10D3)
                return "Intel 82574L E1000E Ethernet (QEMU; driver not loaded)";
            if (subclass == 0x00)
                return "Ethernet controller (driver not loaded)";
            if (subclass == 0x80)
                return "Other network controller (driver not loaded)";
            return "Network controller (driver not loaded)";
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
