using System;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.FileSystem;
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
        private Fat32Allocation.Scan allocationScan;
        private int allocationRow;

        public override void Update()
        {
            if (!IsRunning || Window == null || !Window.Visible || Window.IsMinimized || allocationScan == null)
                return;
            // At most eight reads (four sectors from each mirrored FAT) per update.
            // This bounds I/O count, not driver latency. Rendering performs no I/O.
            if (!allocationScan.TryAdvance(4, out Fat32Space space, out string error))
            {
                descriptions[allocationRow] = error;
                values[allocationRow] = "NIEDOSTEPNE";
                allocationScan = null;
            }
            else if (allocationScan.IsComplete)
            {
                descriptions[allocationRow] = "Zajete " + SpaceMiB(space.AllocatedBytes) +
                    " | uszkodzone " + SpaceMiB(space.BadBytes);
                values[allocationRow] = "Wolne " + SpaceMiB(space.FreeBytes);
                allocationScan = null;
            }
            else
            {
                values[allocationRow] = "SKAN " +
                    (allocationScan.CompletedFatSectors * 100 / allocationScan.TotalFatSectors) + "%";
            }
        }


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
            // Dropping the session cancels it; switching view/F5 never reuses totals.
            allocationScan = null;
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
            foreach (VfsMount mount in VfsManager.Mounts)
            {
                if (itemCount >= MaxItems)
                    break;
                names[itemCount] = mount.MountPoint;
                descriptions[itemCount] = "VFS " + mount.Name;
                values[itemCount] = "SRC " + mount.Source;
                itemCount++;
            }

            // Start on selection/F5, then advance through Update, never RenderContent.
            if (itemCount < MaxItems)
            {
                names[itemCount] = "ROOT FAT32 - KLASTRY DANYCH";
                allocationRow = itemCount;
                if (Disk.TryBeginRootSpaceScan(out allocationScan, out string error))
                {
                    descriptions[itemCount] = "Odczyt porcjami; wynik po zakonczeniu";
                    values[itemCount] = "SKAN 0%";
                }
                else
                {
                    descriptions[itemCount] = error;
                    values[itemCount] = "NIEDOSTEPNE";
                }
                itemCount++;
            }
            if (itemCount < MaxItems)
            {
                names[itemCount] = "ODCZYT FAT Z DYSKU";
                descriptions[itemCount] = "Bez metadanych; zapis w trakcie skanu moze zmienic wynik";
                values[itemCount] = "F5: nowy skan";
                itemCount++;
            }
        }

        private static string SpaceMiB(ulong bytes)
        {
            return ((double)bytes / (1024 * 1024)).ToString("0.##") + " MiB";
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

        protected override void OnKeyboard(KeyEvent key)
        {
            if (key.Key == ConsoleKeyEx.RightArrow)
            {
                if (offset + 6 < itemCount)
                    offset++;
            }
            else if (key.Key == ConsoleKeyEx.LeftArrow)
            {
                if (offset > 0)
                    offset--;
            }
        }
    }
}
