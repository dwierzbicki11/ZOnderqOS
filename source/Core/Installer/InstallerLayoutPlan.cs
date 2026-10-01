using System;

namespace ZonderqOS.SystemCore.Installer
{
    public enum InstallerLayoutStatus
    {
        Ready = 0,
        UnsupportedBlockSize = 1,
        TooSmall = 2,
        TooLargeForMbr = 3
    }

    public sealed class InstallerLayoutPlan
    {
        public const uint StartLba = 2048;

        public InstallerLayoutStatus Status { get; }
        public ulong BlockCount { get; }
        public uint BlockSize { get; }
        public ulong PartitionBlocks { get; }
        public ulong TotalBytes { get; }
        public ulong PartitionBytes { get; }

        public bool IsReady => Status == InstallerLayoutStatus.Ready;

        private InstallerLayoutPlan(
            InstallerLayoutStatus status,
            ulong blockCount,
            uint blockSize,
            ulong partitionBlocks,
            ulong totalBytes,
            ulong partitionBytes)
        {
            Status = status;
            BlockCount = blockCount;
            BlockSize = blockSize;
            PartitionBlocks = partitionBlocks;
            TotalBytes = totalBytes;
            PartitionBytes = partitionBytes;
        }

        public static InstallerLayoutPlan Create(ulong blockCount, uint blockSize)
        {
            ulong totalBytes = MultiplySaturated(blockCount, blockSize);

            if (blockSize < 512 || blockSize > 4096 || (blockSize & (blockSize - 1)) != 0)
            {
                return new InstallerLayoutPlan(
                    InstallerLayoutStatus.UnsupportedBlockSize,
                    blockCount,
                    blockSize,
                    0,
                    totalBytes,
                    0);
            }

            if (blockCount <= StartLba)
            {
                return new InstallerLayoutPlan(
                    InstallerLayoutStatus.TooSmall,
                    blockCount,
                    blockSize,
                    0,
                    totalBytes,
                    0);
            }

            ulong available = blockCount - StartLba;
            if (available > uint.MaxValue)
            {
                return new InstallerLayoutPlan(
                    InstallerLayoutStatus.TooLargeForMbr,
                    blockCount,
                    blockSize,
                    available,
                    totalBytes,
                    MultiplySaturated(available, blockSize));
            }

            return new InstallerLayoutPlan(
                InstallerLayoutStatus.Ready,
                blockCount,
                blockSize,
                available,
                totalBytes,
                MultiplySaturated(available, blockSize));
        }

        public string StatusText()
        {
            switch (Status)
            {
                case InstallerLayoutStatus.Ready:
                    return "ready";
                case InstallerLayoutStatus.UnsupportedBlockSize:
                    return "unsupported block size";
                case InstallerLayoutStatus.TooSmall:
                    return "disk too small";
                case InstallerLayoutStatus.TooLargeForMbr:
                    return "disk too large for current MBR layout";
                default:
                    return "unknown";
            }
        }

        private static ulong MultiplySaturated(ulong a, ulong b)
        {
            if (a == 0 || b == 0)
                return 0;
            if (a > ulong.MaxValue / b)
                return ulong.MaxValue;
            return a * b;
        }
    }
}
