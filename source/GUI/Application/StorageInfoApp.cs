using System;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.Vfs;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class StorageInfoApp : SettingsToolWindow
    {
        private const int MaxItems = 16;
        private readonly string[] names = new string[MaxItems];
        private readonly string[] descriptions = new string[MaxItems];
        private readonly string[] values = new string[MaxItems];
        private int itemCount;
        private int viewMode; // 0 devices, 1 partitions, 2 mounts
        private int offset;

        public StorageInfoApp(int x, int y)
            : base("Dyski i partycje", "Magazyn danych - ZOnderqOS", x, y, 940, 620)
        {
            RefreshData();
        }

        protected override void RenderContent(Canvas canvas)
        {
            string mode = viewMode == 0 ? "URZADZENIA" : viewMode == 1 ? "PARTYCJE" : "MONTOWANIA";
            DrawRow(canvas, 0, IconType.Folder,
                "WIDOK", "Kliknij aby przejsc: urzadzenia / partycje / montowania", mode, Accent);

            for (int row = 1; row <= 6; row++)
            {
                int index = offset + row - 1;
                if (index < itemCount)
                {
                    DrawRow(canvas, row, IconType.Folder,
                        names[index] ?? "N/A", descriptions[index] ?? string.Empty,
                        values[index] ?? string.Empty, Text);
                }
                else
                {
                    DrawRow(canvas, row, IconType.File,
                        row == 1 && itemCount == 0 ? "BRAK DANYCH" : "-",
                        row == 1 && itemCount == 0 ? "Brak elementow w tym widoku" : string.Empty,
                        string.Empty, Muted);
                }
            }
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
                    LoadDevices();
                else if (viewMode == 1)
                    LoadPartitions();
                else
                    LoadMounts();
            }
            catch
            {
                itemCount = 0;
            }

            int maxOffset = System.Math.Max(0, itemCount - 6);
            if (offset > maxOffset)
                offset = maxOffset;
        }

        private void LoadDevices()
        {
            int count = StorageManager.DeviceCount;
            for (int i = 0; i < count && itemCount < MaxItems; i++)
            {
                var device = StorageManager.GetDevice(i);
                if (device == null)
                    continue;

                ulong sizeMb = device.BlockCount * device.BlockSize / 1024UL / 1024UL;
                names[itemCount] = device.Name;
                descriptions[itemCount] = "BLOCK DEVICE  |  BLOK " + device.BlockSize + " B";
                values[itemCount] = sizeMb + " MB";
                itemCount++;
            }
        }

        private void LoadPartitions()
        {
            int count = StorageManager.Partitions.Count;
            for (int i = 0; i < count && itemCount < MaxItems; i++)
            {
                Partition part = StorageManager.Partitions[i];
                ulong sizeMb = part.BlockCount * part.BlockSize / 1024UL / 1024UL;
                string mount = global::ZonderqOS.GUI.StorageMountManager.GetMountPoint(i);
                names[itemCount] = part.Name;
                descriptions[itemCount] = "HOST " + part.Host.Name + "  |  LBA " + part.StartSector;
                values[itemCount] = string.IsNullOrEmpty(mount) ? sizeMb + " MB" : sizeMb + " MB  " + mount;
                itemCount++;
            }
        }

        private void LoadMounts()
        {
            foreach (VfsManager.VfsMount mount in VfsManager.Mounts)
            {
                if (itemCount >= MaxItems)
                    break;
                names[itemCount] = mount.MountPoint;
                descriptions[itemCount] = "VFS " + mount.Name;
                values[itemCount] = "SRC " + mount.Source;
                itemCount++;
            }
        }

        protected override void OnClick(int mouseX, int mouseY)
        {
            int row = HitRow(mouseX, mouseY, 7);
            if (row == 0)
            {
                viewMode++;
                if (viewMode > 2)
                    viewMode = 0;
                offset = 0;
                RefreshData();
                SetStatus("ZMIENIONO WIDOK MAGAZYNU", Accent);
                return;
            }

            if (row >= 1 && row <= 6)
            {
                int index = offset + row - 1;
                if (index < itemCount)
                    SetStatus("ELEMENT WYBRANY - DANE SA TYLKO DO ODCZYTU", Good);
            }
        }

        protected override void OnKeyboard(Cosmos.Kernel.System.Keyboard.KeyEvent key)
        {
            if (key.Key == Cosmos.Kernel.System.Keyboard.ConsoleKeyEx.RightArrow)
            {
                if (offset + 6 < itemCount)
                    offset++;
            }
            else if (key.Key == Cosmos.Kernel.System.Keyboard.ConsoleKeyEx.LeftArrow)
            {
                if (offset > 0)
                    offset--;
            }
        }
    }
}
