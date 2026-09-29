#if ZONDERQ_HTOP_SMOKE_TEST
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.Core.Scheduler;
#pragma warning disable COSMOS0001

namespace ZonderqOS.SystemCore
{
    internal static partial class HtopSmokeTest
    {
        [LibraryImport("*", EntryPoint = "__cosmos_serial_write", StringMarshalling = StringMarshalling.Utf8)]
        private static partial void WriteSerial(string value);
        private static int started;
        private static int release;

        internal static void Run()
        {
            try
            {
                int cpuCount = checked((int)SchedulerInfo.CpuCount);
                int initialThreads = SchedulerInfo.ThreadCount;
                int initialProcesses = ProcessManager.GetActiveProcesses().Count;
                long deadline = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;
                for (int index = 0; index < cpuCount; index++)
                    ProcessManager.Start("htop-smoke-" + index, () =>
                    {
                        Interlocked.Increment(ref started);
                        while (Volatile.Read(ref release) == 0) Thread.SpinWait(64);
                    });
                while (Volatile.Read(ref started) != cpuCount)
                {
                    Require(Stopwatch.GetTimestamp() < deadline, "process startup timeout");
                    Thread.SpinWait(64);
                }
                HtopMonitor monitor = new HtopMonitor();
                monitor.Refresh();
                Thread.Sleep(1000);
                monitor.Refresh();
                ulong expected = (1UL << cpuCount) - 1;
                Require(monitor.CpuCount == cpuCount, "CPU count");
                Require(monitor.ActiveCpuMask == expected, "busy CPU mask=" + monitor.ActiveCpuMask.ToString("X"));
                int names = 0;
                foreach (string line in monitor.Lines) if (line.Contains("htop-smoke-")) names++;
                Require(names == cpuCount, "process/thread identity");

                string path = "/";
                Command.Initialize();
                CommandIO.StartRedirection();
                string output;
                try { Command.Run("htop --once", ref path); }
                finally { output = CommandIO.EndRedirection(); }
                Require(CommandIO.LastCommandSuccess && output.Contains("htop-smoke-") && output.Contains("CPU%"), "htop --once");
                CommandIO.StartRedirection();
                try { Command.Run("htop", ref path); }
                finally { output = CommandIO.EndRedirection(); }
                Require(CommandIO.LastCommandSuccess && output.Contains("Mem:"), "redirected htop");

                Volatile.Write(ref release, 1);
                while (SchedulerInfo.ThreadCount != initialThreads || ProcessManager.GetActiveProcesses().Count != initialProcesses)
                {
                    Require(Stopwatch.GetTimestamp() < deadline, "process exit timeout");
                    Thread.Sleep(10);
                }
                using (SchedulerManager.MaskInterrupts())
                    WriteSerial("[HTOP-TEST] PASS cpus=" + cpuCount + " mask=0x" + expected.ToString("X") + " processes=restored\n");
            }
            catch (Exception ex)
            {
                Volatile.Write(ref release, 1);
                using (SchedulerManager.MaskInterrupts())
                    WriteSerial("[HTOP-TEST] FAIL " + ex.Message + "\n");
                throw;
            }
        }

        private static void Require(bool valid, string reason)
        {
            if (!valid) throw new InvalidOperationException(reason);
        }
    }
}
#endif
