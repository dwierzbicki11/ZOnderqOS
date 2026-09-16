using System;
using System.Runtime.InteropServices;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Architecture-neutral bridge to the CPU utilization entry point exported
    /// by Cosmos CoreLib. The same symbol exists in both x64 and ARM64 builds,
    /// so architecture selection belongs in the project file instead of #if blocks.
    /// </summary>
    internal static class PortableRuntimeTelemetry
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct CpuUtilizationState
        {
            internal ulong LastRecordedCurrentTime;
            internal ulong LastRecordedKernelTime;
            internal ulong LastRecordedUserTime;
        }

        [DllImport("libSystem.Native", EntryPoint = "SystemNative_GetCpuUtilization")]
        private static extern double GetCpuUtilizationNative(ref CpuUtilizationState previousCpuInfo);

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
            SampleCpuPercent(ref state);
            return state.LastRecordedUserTime;
        }
    }
}
