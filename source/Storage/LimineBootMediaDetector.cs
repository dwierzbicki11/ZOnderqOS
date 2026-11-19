using System;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Boot.Limine;
using Cosmos.Kernel.HAL.Devices.Storage;

namespace ZonderqOS.Storage
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct LimineUuid
    {
        public uint A;
        public ushort B;
        public ushort C;
        public fixed byte D[8];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct LimineBootFile
    {
        public ulong Revision;
        public void* Address;
        public ulong Size;
        public byte* Path;
        public byte* Cmdline;
        public uint MediaType;
        public uint Unused;
        public uint TftpIp;
        public uint TftpPort;
        public uint PartitionIndex;
        public uint MbrDiskId;
        public LimineUuid GptDiskUuid;
        public LimineUuid GptPartUuid;
        public LimineUuid PartUuid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct LimineExecutableFileResponse
    {
        public ulong Revision;
        public LimineBootFile* ExecutableFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct LimineExecutableFileRequest
    {
        public LimineID ID;
        public ulong Revision;
        public LimineExecutableFileResponse* Response;
    }

    /// <summary>
    /// Queries Limine boot protocol structures for firmware mode (UEFI vs Legacy BIOS),
    /// the in-RAM booted kernel ELF payload, and the boot medium's hardware identifiers.
    /// </summary>
    public static unsafe class LimineBootMediaDetector
    {
        public const uint MediaTypeGeneric = 0;
        public const uint MediaTypeOptical = 1;
        public const uint MediaTypeTftp = 2;

        // Placed in .data by NativeAOT static preinitialization; scanned by Limine at boot.
        public static readonly LimineExecutableFileRequest ExecutableFile = new LimineExecutableFileRequest
        {
            ID = new LimineID(0xad97e90e83f1ed67UL, 0x31eb5d1c5ff23b69UL),
            Revision = 0,
            Response = null
        };

        private static bool initialized;
        private static bool hasExecutableFile;
        private static IntPtr kernelElfAddress;
        private static int kernelElfSize;
        private static string bootFilePath = string.Empty;
        private static string bootCmdline = string.Empty;
        private static uint bootMediaType;
        private static uint bootMbrDiskId;

        public static void Initialize()
        {
            if (initialized)
                return;
            initialized = true;

            try
            {
                LimineExecutableFileResponse* resp = ExecutableFile.Response;
                if (resp != null && resp->ExecutableFile != null)
                {
                    LimineBootFile* file = resp->ExecutableFile;
                    bootMediaType = file->MediaType;
                    bootMbrDiskId = file->MbrDiskId;
                    bootFilePath = ReadAsciiString(file->Path);
                    bootCmdline = ReadAsciiString(file->Cmdline);

                    if (file->Address != null && file->Size >= 1024UL && file->Size <= 32UL * 1024UL * 1024UL)
                    {
                        byte* ptr = (byte*)file->Address;
                        if (ptr[0] == 0x7F && ptr[1] == (byte)'E' && ptr[2] == (byte)'L' && ptr[3] == (byte)'F')
                        {
                            kernelElfAddress = (IntPtr)ptr;
                            kernelElfSize = (int)file->Size;
                            hasExecutableFile = true;
                        }
                    }
                }
            }
            catch
            {
                hasExecutableFile = false;
            }
        }

        public static bool IsUefiBoot
        {
            get
            {
                try
                {
                    return Limine.EfiSystemTable.Response != null &&
                           Limine.EfiSystemTable.Response->Address != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static string FirmwareModeName => IsUefiBoot ? "UEFI" : "Legacy BIOS";

        public static bool IsOpticalBootMedia
        {
            get
            {
                Initialize();
                return bootMediaType == MediaTypeOptical;
            }
        }

        public static uint BootMbrDiskId
        {
            get
            {
                Initialize();
                return bootMbrDiskId;
            }
        }

        public static string BootFilePath
        {
            get
            {
                Initialize();
                return bootFilePath ?? string.Empty;
            }
        }

        public static string BootCommandLine
        {
            get
            {
                Initialize();
                if (!string.IsNullOrEmpty(bootCmdline))
                    return bootCmdline;

                try
                {
                    return ReadAsciiString(Limine.Cmdline);
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        public static bool IsLiveModeRequestedByCmdline()
        {
            string cmd = BootCommandLine;
            if (string.IsNullOrEmpty(cmd))
                return false;

            return cmd.IndexOf("live", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   cmd.IndexOf("installer", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool TryGetBootKernelElf(out IntPtr address, out int size)
        {
            Initialize();
            address = kernelElfAddress;
            size = kernelElfSize;
            return hasExecutableFile && address != IntPtr.Zero && size >= 1024;
        }

        public static uint ReadDeviceMbrSignature(IBlockDevice device)
        {
            if (device == null || device.BlockCount == 0 || device.BlockSize < 512 || device.BlockSize > 4096)
                return 0;

            try
            {
                byte[] sector0 = new byte[(int)device.BlockSize];
                device.ReadBlock(0UL, 1UL, sector0);
                return (uint)(sector0[440] |
                              (sector0[441] << 8) |
                              (sector0[442] << 16) |
                              (sector0[443] << 24));
            }
            catch
            {
                return 0;
            }
        }

        public static bool IsBootOrInstallationMedia(IBlockDevice device)
        {
            if (device == null)
                return false;

            Initialize();

            // 1. Check if the device is a hybrid ISO / optical image (CD001 at 32768)
            if (FilesystemSignatureProbe.HasIso9660Signature(device))
                return true;

#pragma warning disable COSMOS0003
            // 2. Check if the device is marked read-only removable media
            if (device is IRemovableMedia removable && removable.IsReadOnly)
                return true;
#pragma warning restore COSMOS0003

            // 3. Check if Limine booted from this disk's MBR signature (generic disk/USB boot).
            if (bootMediaType == MediaTypeGeneric && bootMbrDiskId != 0)
            {
                uint sig = ReadDeviceMbrSignature(device);
                if (sig != 0 && sig == bootMbrDiskId)
                    return true;
            }

            return false;
        }

        private static string ReadAsciiString(byte* ptr)
        {
            if (ptr == null)
                return string.Empty;

            int len = 0;
            while (len < 256 && ptr[len] != 0)
                len++;

            if (len == 0)
                return string.Empty;

            char[] chars = new char[len];
            for (int i = 0; i < len; i++)
            {
                byte b = ptr[i];
                chars[i] = (b >= 32 && b <= 126) ? (char)b : '?';
            }
            return new string(chars);
        }
    }
}
