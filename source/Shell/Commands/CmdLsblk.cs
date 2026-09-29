using System;
using Cosmos.Kernel.System.Storage;

namespace ZonderqOS.Commands
{
    public sealed class CmdLsblk : ICommand
    {
        public string Name => "lsblk";
        public string Description => "List block devices and partitions";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 1)
            {
                CommandIO.WriteLine("Usage: lsblk");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            try
            {
                CommandIO.WriteLine("NAME         TYPE        SIZE        BLOCKS     BLKSZ");

                int devices = Math.Max(0, StorageManager.DeviceCount);
                for (int i = 0; i < devices; i++)
                {
                    var device = StorageManager.GetDevice(i);
                    ulong blocks = device.BlockCount;
                    ulong blockSize = (ulong)device.BlockSize;
                    ulong bytes = MultiplySaturated(blocks, blockSize);
                    string name = string.IsNullOrWhiteSpace(device.Name) ? "disk" + i : device.Name;

                    PrintRow(name, "disk", bytes, blocks, blockSize);
                }

                var partitions = StorageManager.Partitions;
                int partitionCount = partitions == null ? 0 : Math.Max(0, partitions.Count);
                for (int i = 0; i < partitionCount; i++)
                {
                    var part = partitions[i];
                    ulong blocks = part.BlockCount;
                    ulong blockSize = (ulong)part.BlockSize;
                    ulong bytes = MultiplySaturated(blocks, blockSize);
                    PrintRow("part" + i, "part", bytes, blocks, blockSize);
                }

                CommandIO.WriteLine("Devices: " + devices + " | partitions: " + partitionCount);
                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                CommandIO.WriteLine("lsblk: " + ex.Message);
                CommandIO.LastCommandSuccess = false;
            }
        }

        private static void PrintRow(string name, string type, ulong bytes, ulong blocks, ulong blockSize)
        {
            CommandIO.WriteLine(
                (name ?? "?").PadRight(13) +
                type.PadRight(12) +
                FormatBytes(bytes).PadRight(12) +
                blocks.ToString().PadRight(11) +
                blockSize);
        }

        private static ulong MultiplySaturated(ulong a, ulong b)
        {
            if (a == 0 || b == 0) return 0;
            if (a > ulong.MaxValue / b) return ulong.MaxValue;
            return a * b;
        }

        private static string FormatBytes(ulong bytes)
        {
            if (bytes >= 1099511627776UL) return ((double)bytes / 1099511627776d).ToString("0.0") + "T";
            if (bytes >= 1073741824UL) return ((double)bytes / 1073741824d).ToString("0.0") + "G";
            if (bytes >= 1048576UL) return ((double)bytes / 1048576d).ToString("0.0") + "M";
            if (bytes >= 1024UL) return ((double)bytes / 1024d).ToString("0.0") + "K";
            return bytes + "B";
        }
    }
}
