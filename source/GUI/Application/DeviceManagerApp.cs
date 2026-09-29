using System;
using System.Collections.Generic;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Storage;
using ZonderqOS.GUI.Icons;
using ZonderqOS.Hardware;

namespace ZonderqOS.GUI.Apps
{
    public sealed class DeviceManagerApp : SettingsToolWindow
    {
        private const int MaxItems = 8;
        private readonly string[] names = new string[MaxItems];
        private readonly string[] descriptions = new string[MaxItems];
        private readonly string[] values = new string[MaxItems];
        private int itemCount;
        private int viewMode; // 0 PCI, 1 storage, 2 network
        private int selectedIndex;

        public DeviceManagerApp(int x, int y)
            : base("Menedzer urzadzen", "Urządzenia systemowe - ZOnderqOS", x, y, 940, 620)
        {
            RefreshData();
        }

        protected override void RenderContent(Canvas canvas)
        {
            DrawRow(canvas, 0, IconType.Settings,
                "KATEGORIA", "Kliknij aby przelaczyc PCI / magazyn / siec",
                ViewModeName(), Accent);

            for (int row = 1; row <= 5; row++)
            {
                int index = row - 1;
                if (index < itemCount)
                {
                    bool selected = index == selectedIndex;
                    DrawRow(canvas, row, viewMode == 1 ? IconType.Folder : IconType.Settings,
                        names[index], descriptions[index], values[index], selected ? Accent : Text);
                }
                else
                {
                    DrawRow(canvas, row, IconType.File,
                        index == 0 && itemCount == 0 ? "BRAK URZADZEN" : "-",
                        string.Empty, string.Empty, Muted);
                }
            }

            bool actionAvailable = viewMode == 0 || itemCount > 0;
            DrawRow(canvas, 6, IconType.Refresh,
                viewMode == 0 ? "PONOWNIE SKANUJ PCI" :
                viewMode == 1 ? "PONOWNIE SKANUJ PARTYCJE" : "USTAW AKTYWNY INTERFEJS",
                viewMode == 0 ? "Odswiez kernelowy rejestr funkcji PCI/PCIe" :
                viewMode == 1 ? "Bez formatowania; tylko ponowny odczyt tablicy partycji" :
                "Zastosuj zapisany profil IPv4 na wybranej karcie",
                actionAvailable ? "WYKONAJ" : "BRAK", actionAvailable ? Warning : Muted);
        }

        protected override void RefreshData()
        {
            for (int i = 0; i < MaxItems; i++)
            {
                names[i] = null;
                descriptions[i] = null;
                values[i] = null;
            }
            itemCount = 0;

            try
            {
                if (viewMode == 0)
                {
                    var devices = new List<DeviceDescriptor>();
                    HardwareDeviceRegistry.CopyPciDevices(devices);
                    for (int i = 0; i < devices.Count && itemCount < MaxItems; i++)
                    {
                        DeviceDescriptor device = devices[i];
                        names[itemCount] = device.Id.Address;
                        descriptions[itemCount] =
                            device.VendorId.ToString("X4") + ":" + device.DeviceId.ToString("X4") +
                            "  CLASS " + device.ClassCode.ToString("X2") + ":" + device.Subclass.ToString("X2");
                        values[itemCount] = PciClassName(device.ClassCode);
                        itemCount++;
                    }
                }
                else if (viewMode == 1)
                {
                    int count = StorageManager.DeviceCount;
                    for (int i = 0; i < count && itemCount < MaxItems; i++)
                    {
                        var device = StorageManager.GetDevice(i);
                        if (device == null)
                            continue;

                        ulong sizeMb = device.BlockCount * device.BlockSize / 1024UL / 1024UL;
                        names[itemCount] = device.Name;
                        descriptions[itemCount] = "BLOCK " + device.BlockSize + " B";
                        values[itemCount] = sizeMb + " MB";
                        itemCount++;
                    }
                }
                else
                {
                    int count = global::ZonderqOS.Network.Devices.Count;
                    for (int i = 0; i < count && itemCount < MaxItems; i++)
                    {
                        var device = global::ZonderqOS.Network.Devices[i];
                        if (device == null)
                            continue;

                        names[itemCount] = device.Name;
                        descriptions[itemCount] = "MAC " + device.MacAddress;
                        bool active = device == global::ZonderqOS.Network.ActiveDevice;
                        values[itemCount] = active ? "AKTYWNY" : device.LinkUp ? "LINK UP" : device.Ready ? "GOTOWY" : "OFFLINE";
                        if (active)
                            selectedIndex = itemCount;
                        itemCount++;
                    }
                }
            }
            catch
            {
                itemCount = 0;
            }

            if (selectedIndex >= itemCount)
                selectedIndex = itemCount > 0 ? itemCount - 1 : 0;
        }

        private string ViewModeName()
        {
            if (viewMode == 0) return "PCI";
            if (viewMode == 1) return "MAGAZYN";
            return "SIEC";
        }

        private static string PciClassName(byte classCode)
        {
            switch (classCode)
            {
                case 0x01: return "STORAGE";
                case 0x02: return "NETWORK";
                case 0x03: return "DISPLAY";
                case 0x04: return "MULTIMEDIA";
                case 0x05: return "MEMORY";
                case 0x06: return "BRIDGE";
                case 0x07: return "COMM";
                case 0x08: return "SYSTEM";
                case 0x0C: return "SERIAL BUS";
                default: return "PCI";
            }
        }

        protected override void OnClick(int mouseX, int mouseY)
        {
            int row = HitRow(mouseX, mouseY, 7);
            if (row == 0)
            {
                viewMode = (viewMode + 1) % 3;
                selectedIndex = 0;
                RefreshData();
                SetStatus("ZMIENIONO KATEGORIE URZADZEN", Accent);
                return;
            }

            if (row >= 1 && row <= 5)
            {
                int index = row - 1;
                if (index < itemCount)
                {
                    selectedIndex = index;
                    SetStatus("WYBRANO URZADZENIE", Good);
                }
                return;
            }

            if (row != 6)
                return;

            if (viewMode == 0)
            {
                HardwareDeviceRegistry.Initialize();
                RefreshData();
                SetStatus(string.IsNullOrEmpty(HardwareDeviceRegistry.LastError)
                    ? "PONOWNIE ODCZYTANO MAGISTRALE PCI"
                    : "BLAD SKANOWANIA PCI: " + HardwareDeviceRegistry.LastError,
                    string.IsNullOrEmpty(HardwareDeviceRegistry.LastError) ? Good : Danger);
            }
            else if (viewMode == 1)
            {
                if (itemCount <= 0)
                    return;

                try
                {
                    var device = StorageManager.GetDevice(selectedIndex);
                    if (device == null)
                    {
                        SetStatus("BRAK WYBRANEGO DYSKU", Danger);
                        return;
                    }
                    StorageManager.RescanPartitions(device);
                    RefreshData();
                    SetStatus("PONOWNIE ODCZYTANO TABLICE PARTYCJI", Good);
                }
                catch
                {
                    SetStatus("BLAD PONOWNEGO SKANOWANIA DYSKU", Danger);
                }
            }
            else
            {
                if (itemCount <= 0)
                    return;

                bool ok = false;
                try
                {
                    ok = global::ZonderqOS.Network.SetActiveDevice(selectedIndex);
                }
                catch
                {
                    ok = false;
                }
                RefreshData();
                SetStatus(ok ? "AKTYWNY INTERFEJS ZMIENIONY" : "NIE UDALO SIE SKONFIGUROWAC KARTY",
                    ok ? Good : Danger);
            }
        }
    }
}
