#if ARCH_ARM64
using System;
using System.Runtime.InteropServices;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// ARM64 bridge to the CPU utilization entry point already exported by
    /// Cosmos.Kernel.Core for CoreLib. The native implementation samples the
    /// real scheduler busy-time counter, so this is not a synthetic graph.
    /// </summary>
    internal static partial class PortableRuntimeTelemetry
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct CpuUtilizationState
        {
            internal ulong LastRecordedCurrentTime;
            internal ulong LastRecordedKernelTime;
            internal ulong LastRecordedUserTime;
        }

        [LibraryImport("libSystem.Native", EntryPoint = "SystemNative_GetCpuUtilization")]
        private static partial double GetCpuUtilizationNative(ref CpuUtilizationState previousCpuInfo);

        internal static int SampleCpuPercent(ref CpuUtilizationState state)
        {
            try
            {
                double value = GetCpuUtilizationNative(ref state);
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0)
                    return 0;
                if (value >= 100.0)
                    return 100;
                return (int)(value + 0.5);
            }
            catch
            {
                return 0;
            }
        }

        internal static ulong SampleBusyCpuTimeNs(ref CpuUtilizationState state)
        {
            // The native helper stores SchedulerManager.GetBusyCpuTimeNs() in
            // LastRecordedUserTime whenever it takes a sample. A call made too
            // soon simply leaves the previous real value in place.
            SampleCpuPercent(ref state);
            return state.LastRecordedUserTime;
        }
    }
}
#endif
