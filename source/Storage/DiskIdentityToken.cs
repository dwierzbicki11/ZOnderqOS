using System;
using Cosmos.Kernel.HAL.Devices.Storage;

namespace ZonderqOS.Storage
{
    /// <summary>
    /// Immutable cryptographic and hardware identity token captured when a user selects
    /// a target disk. Re-verified immediately before any destructive write so device
    /// renumbering or hotplug events can never redirect a format operation to another disk.
    /// </summary>
    public sealed class DiskIdentityToken
    {
        public string DiskId { get; }
        public IBlockDevice DeviceReference { get; }
        public string DeviceName { get; }
        public ulong BlockCount { get; }
        public ulong BlockSize { get; }
        public uint MbrSignature { get; }
        public string Sector0Sha256 { get; }
        public string Sector1Sha256 { get; }

        public DiskIdentityToken(
            string diskId,
            IBlockDevice deviceReference,
            string deviceName,
            ulong blockCount,
            ulong blockSize,
            uint mbrSignature,
            string sector0Sha256,
            string sector1Sha256)
        {
            DiskId = diskId ?? string.Empty;
            DeviceReference = deviceReference;
            DeviceName = deviceName ?? string.Empty;
            BlockCount = blockCount;
            BlockSize = blockSize;
            MbrSignature = mbrSignature;
            Sector0Sha256 = sector0Sha256 ?? string.Empty;
            Sector1Sha256 = sector1Sha256 ?? string.Empty;
        }

        public bool Matches(
            IBlockDevice currentDevice,
            string deviceName,
            ulong blockCount,
            ulong blockSize,
            uint mbrSignature,
            string sector0Sha256,
            string sector1Sha256,
            out string mismatchReason)
        {
            mismatchReason = null;

            if (DeviceReference == null || currentDevice == null || !object.ReferenceEquals(DeviceReference, currentDevice))
            {
                mismatchReason = "Target block device object reference changed.";
                return false;
            }

            if (!string.Equals(DeviceName, deviceName ?? string.Empty, StringComparison.Ordinal))
            {
                mismatchReason = "Target device name does not match the selected disk.";
                return false;
            }

            if (BlockCount != blockCount || BlockCount == 0)
            {
                mismatchReason = "Target disk sector count does not match the selected disk.";
                return false;
            }

            if (BlockSize != blockSize || BlockSize < 512)
            {
                mismatchReason = "Target disk block size does not match the selected disk.";
                return false;
            }

            if (MbrSignature != mbrSignature)
            {
                mismatchReason = "Target disk MBR signature changed after selection.";
                return false;
            }

            if (string.IsNullOrEmpty(Sector0Sha256) ||
                !string.Equals(Sector0Sha256, sector0Sha256 ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                mismatchReason = "Target disk Sector 0 SHA-256 fingerprint changed after selection.";
                return false;
            }

            if (string.IsNullOrEmpty(Sector1Sha256) ||
                !string.Equals(Sector1Sha256, sector1Sha256 ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                mismatchReason = "Target disk Sector 1 SHA-256 fingerprint changed after selection.";
                return false;
            }

            return true;
        }
    }
}
