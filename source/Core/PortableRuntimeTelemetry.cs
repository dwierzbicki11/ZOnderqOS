using System;
using System.Runtime.InteropServices;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Architecture-neutral bridge to the CPU utilization entry point already
    /// exported by Cosmos.Kernel.Core for CoreLib. This avoids depending on the
    /// optional Cosmos.Kernel.System.Diagnostics facade on ARM64 while still
    /// reporting scheduler-backed, non-fabricated CPU utilization.
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
    }
}
