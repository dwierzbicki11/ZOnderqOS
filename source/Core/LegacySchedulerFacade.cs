using Cosmos.Kernel.System.Diagnostics;

namespace ZonderqOS.SystemCore
{
    internal sealed class SchedulerDescriptor
    {
        internal SchedulerDescriptor(string name)
        {
            Name = name ?? string.Empty;
        }

        public string Name { get; }
    }

    /// <summary>
    /// Preserves the small SchedulerManager-shaped surface used by older GUI
    /// panels while all real data comes from the cross-architecture diagnostics
    /// compatibility layer.
    /// </summary>
    internal static class LegacySchedulerFacade
    {
        public static bool IsReady => SchedulerInfo.IsInitialized;
        public static int ThreadCount => SchedulerInfo.ThreadCount;
        public static uint CpuCount => SchedulerInfo.CpuCount;

        public static SchedulerDescriptor Current
        {
            get
            {
                string name = SchedulerInfo.SchedulerName;
                return string.IsNullOrEmpty(name) ? null : new SchedulerDescriptor(name);
            }
        }

        public static ulong GetBusyCpuTimeNs()
        {
            return SchedulerInfo.BusyCpuTimeNs;
        }
    }
}
