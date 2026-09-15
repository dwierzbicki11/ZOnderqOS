using System;
using System.Collections.Generic;
using System.Threading;
using Cosmos.Kernel.System.Diagnostics;

namespace Cosmos.Kernel.System.Network
{
    /// <summary>
    /// Minimal address value used only by the ARM64 boot-smoke build.
    /// The 3.0.84 arm64 package currently lacks the public Address projection
    /// that the x64 package exposes, while ZonderqOS UI code only needs a value
    /// that can be displayed.
    /// </summary>
    public sealed class Address
    {
        private readonly string value;

        private Address(string value)
        {
            this.value = value ?? string.Empty;
        }

        public static Address Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return new Address(value.Trim());
        }

        public override string ToString()
        {
            return value;
        }
    }
}

namespace Cosmos.Kernel.Core.Memory
{
    public static class PageAllocator
    {
        public static ulong TotalPageCount
        {
            get { try { return MemoryInfo.TotalPages; } catch { return 0UL; } }
        }

        public static ulong FreePageCount
        {
            get { try { return MemoryInfo.FreePages; } catch { return 0UL; } }
        }

        public static ulong PageSize
        {
            get { try { return MemoryInfo.PageSizeBytes; } catch { return 4096UL; } }
        }

        public static ulong RamSize
        {
            get { try { return MemoryInfo.RamSizeBytes; } catch { return 0UL; } }
        }
    }
}

namespace Cosmos.Kernel.Core.Memory.GarbageCollector
{
    public static class GarbageCollector
    {
        public static bool IsEnabled => true;

        public static ulong GetHeapSizeBytes()
        {
            try
            {
                long value = GC.GetGCMemoryInfo().HeapSizeBytes;
                return value > 0 ? (ulong)value : 0UL;
            }
            catch { return 0UL; }
        }

        public static ulong GetTotalCommittedBytes()
        {
            try
            {
                long value = GC.GetGCMemoryInfo().TotalCommittedBytes;
                return value > 0 ? (ulong)value : 0UL;
            }
            catch { return 0UL; }
        }

        public static ulong GetFragmentedBytes()
        {
            try
            {
                long value = GC.GetGCMemoryInfo().FragmentedBytes;
                return value > 0 ? (ulong)value : 0UL;
            }
            catch { return 0UL; }
        }

        public static ulong GetPinnedObjectsCount()
        {
            try
            {
                long value = GC.GetGCMemoryInfo().PinnedObjectsCount;
                return value > 0 ? (ulong)value : 0UL;
            }
            catch { return 0UL; }
        }

        public static int GetCollectionIndex()
        {
            try { return MemoryInfo.TotalCollections; }
            catch { return 0; }
        }
    }
}

namespace Cosmos.Kernel.Core.Scheduler
{
    public enum ThreadState
    {
        Created = 0,
        Ready = 1,
        Running = 2,
        Blocked = 3,
        Sleeping = 4,
        Dead = 5
    }

    public sealed class Thread
    {
        public ThreadState State { get; private set; }

        internal Thread(ThreadState state)
        {
            State = state;
        }
    }

    public sealed class SchedulerDescriptor
    {
        public string Name { get; private set; }

        internal SchedulerDescriptor(string name)
        {
            Name = name ?? string.Empty;
        }
    }

    /// <summary>
    /// ARM64 boot-smoke scheduler projection. Avoids KernelThreadState from the
    /// 3.0.84 arm64 reference assembly; the real scheduler can still run in the
    /// kernel, but UI diagnostics report no thread snapshots for this profile.
    /// </summary>
    public static class SchedulerManager
    {
        public static bool IsReady => false;
        public static int ThreadCount => 0;
        public static uint CpuCount => 1;
        public static SchedulerDescriptor Current => null;
        public static ulong GetBusyCpuTimeNs() => 0UL;
        public static Thread[] Threads => new Thread[0];
    }
}

namespace ZonderqOS
{
    /// <summary>
    /// Application-facing network snapshot used by the ARM64 smoke build.
    /// RPi4 BCM2711 networking is intentionally disabled until a real GENET
    /// driver/platform integration exists.
    /// </summary>
    public sealed class NetworkDeviceInfo
    {
        public string Name { get; internal set; } = "Unavailable";
        public string MacAddress { get; internal set; } = "00:00:00:00:00:00";
        public bool LinkUp { get; internal set; }
        public bool Ready { get; internal set; }
        public string IpAddress { get; internal set; } = "0.0.0.0";
        public string SubnetMask { get; internal set; } = "0.0.0.0";
        public string DefaultGateway { get; internal set; } = "0.0.0.0";
    }

    public static class Network
    {
        public static List<NetworkDeviceInfo> Devices { get; private set; } = new List<NetworkDeviceInfo>();
        public static NetworkDeviceInfo ActiveDevice { get; private set; }
        public static bool IsReady { get; private set; }
        public static string CurrentAddress => "0.0.0.0";

        public static void Initialize()
        {
            Devices.Clear();
            ActiveDevice = null;
            IsReady = false;
        }

        public static bool SetActiveDevice(int index) => false;
        public static bool ApplySavedConfiguration() => false;
        public static bool ConfigureDhcp() => false;
        public static bool ConfigureStatic(string ip, string subnet, string gateway, string dns) => false;

        public static void SendUdpTest(string targetIp, int port, string text)
        {
            WriteMessage.WriteError("Network is disabled in the Raspberry Pi 4 ARM64 boot-smoke profile.", "NET");
        }

        public static string ResolveDns(string domain) => null;

        public static void ShowInterfaceInfo()
        {
            WriteMessage.WriteInfo("ARM64/RPi4 boot-smoke profile: networking is disabled.", "NET");
        }
    }

    /// <summary>
    /// Minimal kernel selected only for `cosmos build -a arm64`.
    /// It deliberately avoids disk, input, network, accounts and GUI startup so
    /// we can prove that UEFI -> Limine -> Cosmos -> managed kernel execution works
    /// on a physical Raspberry Pi 4 before adding BCM2711 drivers.
    /// </summary>
    public sealed class Rpi4BootKernel : Cosmos.Kernel.System.Kernel
    {
        private bool announced;

        protected override void BeforeRun()
        {
            Console.Clear();
            Console.WriteLine("========================================");
            Console.WriteLine("  ZonderqOS ARM64 - Raspberry Pi 4");
            Console.WriteLine("  BOOT SMOKE TEST");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("Managed kernel reached BeforeRun().");
            Console.WriteLine("UEFI -> Limine -> Cosmos ARM64: OK");
            Console.WriteLine();
            Console.WriteLine("Storage/network/keyboard/mouse are disabled");
            Console.WriteLine("until BCM2711-specific drivers are available.");
        }

        protected override void Run()
        {
            if (!announced)
            {
                Console.WriteLine();
                Console.WriteLine("Kernel Run() loop is alive.");
                Console.WriteLine("If you can read this, the ARM64 kernel booted on the Pi.");
                announced = true;
            }

            Thread.Sleep(1000);
        }
    }
}

namespace Cosmos.Kernel.System.Network.Config
{
    public static class NetworkConfigManager
    {
        public static global::ZonderqOS.GUI.Apps.IPConfig Get(global::ZonderqOS.NetworkDeviceInfo device)
        {
            if (device == null)
                return null;

            return new global::ZonderqOS.GUI.Apps.IPConfig(
                global::Cosmos.Kernel.System.Network.Address.Parse(device.IpAddress),
                global::Cosmos.Kernel.System.Network.Address.Parse(device.SubnetMask),
                global::Cosmos.Kernel.System.Network.Address.Parse(device.DefaultGateway));
        }

        public static global::Cosmos.Kernel.System.Network.Address CurrentAddress
        {
            get
            {
                global::ZonderqOS.NetworkDeviceInfo active = global::ZonderqOS.Network.ActiveDevice;
                return active == null
                    ? null
                    : global::Cosmos.Kernel.System.Network.Address.Parse(active.IpAddress);
            }
        }
    }
}

namespace ZonderqOS.Commands
{
    public class CmdPing : ICommand
    {
        public string Name => "ping";
        public string Description => "ICMP echo utility (unavailable in ARM64 RPi4 boot-smoke profile)";

        public void Execute(string[] args, ref string currentPath)
        {
            CommandIO.WriteLine("ping: unavailable in the Raspberry Pi 4 ARM64 boot-smoke profile");
            CommandIO.LastCommandSuccess = false;
        }
    }
}
