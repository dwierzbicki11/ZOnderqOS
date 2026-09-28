using System;
using System.Text;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Builds a privacy-conscious diagnostic report from existing read-only
    /// system telemetry. The report deliberately excludes serial numbers,
    /// MAC addresses, usernames, home paths and command history.
    /// </summary>
    public static class DiagnosticReportBuilder
    {
        private const int MaxRecentLogEntries = 32;
        private const int MaxLogMessageLength = 240;

        public static string Build()
        {
            HardwareSnapshot info = HardwareSnapshot.Capture();
            StringBuilder text = new StringBuilder(4096);

            text.AppendLine("=== ZOnderqOS Gen 3 Diagnostic Report ===");
            text.AppendLine("Generated: " + SafeTimestamp());
            text.AppendLine();

            text.AppendLine("[HARDWARE]");
            text.AppendLine("Architecture=" + info.Architecture);
            text.AppendLine("CpuVendor=" + info.CpuVendor);
            text.AppendLine("CpuBrand=" + info.CpuBrand);
            text.AppendLine("PhysicalCores=" + info.PhysicalCores);
            text.AppendLine("LogicalProcessors=" + info.LogicalProcessors);
            text.AppendLine("ThreadsPerCore=" + info.ThreadsPerCore);
            text.AppendLine("OnlineCpuCount=" + info.OnlineCpuCount);
            text.AppendLine("L1Bytes=" + info.L1Bytes);
            text.AppendLine("L2Bytes=" + info.L2Bytes);
            text.AppendLine("L3Bytes=" + info.L3Bytes);
            text.AppendLine("HypervisorPresent=" + Bool(info.HypervisorPresent));
            text.AppendLine("VirtualizationSupported=" + Bool(info.VirtualizationSupported));
            text.AppendLine("CpuFeatures=" + Sanitize(info.CpuFeatures, 512));
            text.AppendLine();

            text.AppendLine("[MEMORY]");
            text.AppendLine("TotalBytes=" + info.TotalMemoryBytes);
            text.AppendLine("UsedBytes=" + info.UsedMemoryBytes);
            text.AppendLine("FreeBytes=" + info.FreeMemoryBytes);
            text.AppendLine();

            text.AppendLine("[SCHEDULER]");
            text.AppendLine("Name=" + Sanitize(info.SchedulerName, 96));
            text.AppendLine("OnlineCpuCount=" + info.OnlineCpuCount);
            text.AppendLine("ThreadSlots=" + info.SchedulerThreadSlots);
            text.AppendLine();

            text.AppendLine("[STORAGE]");
            text.AppendLine("BlockDevices=" + info.StorageDeviceCount);
            text.AppendLine("Partitions=" + info.StoragePartitionCount);
            text.AppendLine();

            text.AppendLine("[RECENT_SYSTEM_LOG]");
            int written = 0;
            for (int i = MaxRecentLogEntries - 1; i >= 0; i--)
            {
                SystemLogEntry entry;
                if (!SystemLogger.TryGetRecent(i, out entry))
                    continue;

                text.AppendLine(
                    "#" + entry.Sequence +
                    " [" + SafeLevel(entry.Level) + "]" +
                    " [" + Sanitize(entry.Source, 16) + "] " +
                    Sanitize(entry.Message, MaxLogMessageLength));
                written++;
            }

            if (written == 0)
                text.AppendLine("(no in-memory log entries)");

            text.AppendLine();
            text.AppendLine("[PRIVACY]");
            text.AppendLine("SerialNumbers=excluded");
            text.AppendLine("MacAddresses=excluded");
            text.AppendLine("UserIdentity=excluded");
            text.AppendLine("CommandHistory=excluded");

            return text.ToString();
        }

        private static string SafeTimestamp()
        {
            try
            {
                DateTime now = DateTime.Now;
                return now.Year.ToString("D4") + "-" +
                       now.Month.ToString("D2") + "-" +
                       now.Day.ToString("D2") + " " +
                       now.Hour.ToString("D2") + ":" +
                       now.Minute.ToString("D2") + ":" +
                       now.Second.ToString("D2");
            }
            catch
            {
                return "TIME_ERROR";
            }
        }

        private static string SafeLevel(SystemLogLevel level)
        {
            switch (level)
            {
                case SystemLogLevel.Debug: return "DEBUG";
                case SystemLogLevel.Warning: return "WARN";
                case SystemLogLevel.Error: return "ERROR";
                case SystemLogLevel.Critical: return "CRITICAL";
                default: return "INFO";
            }
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static string Sanitize(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            string clean = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return clean.Length <= maxLength ? clean : clean.Substring(0, maxLength);
        }
    }
}
