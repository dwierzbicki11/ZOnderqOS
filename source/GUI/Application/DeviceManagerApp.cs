using System;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Storage;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class DeviceManagerApp : SettingsToolWindow
    {
        private const int MaxItems = 8;
        private readonly string[] names = new string[MaxItems];
        private readonly string[] descriptions = new string[MaxItems];
        private readonly string[] values = new string[MaxItems];
        private int itemCount;
        private int viewMode; // 0 network, 1 storage
        private int selectedIndex;

        public DeviceManagerApp(int x, int y)
            : base("Device Manager", "System devices - ZOnderqOS", x, y, 940, 620)
        {
            RefreshData();
        }

        protected override void RenderContent(Canvas canvas)
        {
            DrawRow(canvas, 0, IconType.Settings,
                "CATEGORY", "Click to switch between network and storage devices",
                viewMode == 0 ? "NETWORK" : "STORAGE", Accent);

            for (int row = 1; row <= 5; row++)
            {
                int index = row - 1;
                if (index < itemCount)
                {
                    bool selected = index == selectedIndex;
                    DrawRow(canvas, row, viewMode == 0 ? IconType.Settings : IconType.Folder,
                        names[index], descriptions[index], values[index], selected ? Accent : Text);
                }
                else
                {
                    DrawRow(canvas, row, IconType.File,
                        index == 0 && itemCount == 0 ? "NO DEVICES" : "-",
                        string.Empty, string.Empty, Muted);
                }
            }

            DrawRow(canvas, 6, IconType.Refresh,
                viewMode == 0 ? "USTAW AKTYWNY INTERFEJS" : "PONOWNIE SKANUJ PARTYCJE",
                viewMode == 0 ? "Zastosuj zapisany profil IPv4 na wybranej karcie" : "Bez formatowania; tylko ponowny odczyt tablicy partycji",
                itemCount > 0 ? "EXECUTE" : "NONE", itemCount > 0 ? Warning : Muted);
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
                else
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
            }
            catch
            {
                itemCount = 0;
            }

            if (selectedIndex >= itemCount)
                selectedIndex = itemCount > 0 ? itemCount - 1 : 0;
        }

        protected override void OnClick(int mouseX, int mouseY)
        {
            int row = HitRow(mouseX, mouseY, 7);
            if (row == 0)
            {
                viewMode = viewMode == 0 ? 1 : 0;
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

            if (row != 6 || itemCount <= 0)
                return;

            if (viewMode == 0)
            {
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
                SetStatus(ok ? "ACTIVE INTERFACE CHANGED" : "FAILED TO CONFIGURE ADAPTER",
                    ok ? Good : Danger);
            }
            else
            {
                try
                {
                    var device = StorageManager.GetDevice(selectedIndex);
                    if (device == null)
                    {
                        SetStatus("NO DISK SELECTED", Danger);
                        return;
                    }
                    StorageManager.RescanPartitions(device);
                    RefreshData();
                    SetStatus("PONOWNIE ODCZYTANO TABLICE PARTYCJI", Good);
                }
                catch
                {
                    SetStatus("DISK RESCAN FAILED", Danger);
                }
            }
        }
    }
}
