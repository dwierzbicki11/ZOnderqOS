using System;
using System.IO;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Tracks whether the previous boot session reached a controlled power transition.
    /// A lingering "running" marker means the previous session ended unexpectedly.
    /// </summary>
    public static class BootSessionHealth
    {
        private const string StateDirectory = "/var/lib/zonderq";
        private const string StatePath = StateDirectory + "/boot-session.state";
        private const string TempPath = StateDirectory + "/boot-session.state.new";
        private const string BackupPath = StateDirectory + "/boot-session.state.bak";

        private static bool initialized;

        public static bool HasPreviousSession { get; private set; }
        public static bool PreviousSessionWasClean { get; private set; }
        public static string PreviousExitReason { get; private set; } = "unknown";
        public static string PreviousTimestamp { get; private set; } = "unknown";
        public static bool MarkerAvailable { get; private set; }

        public static void Initialize()
        {
            if (initialized)
                return;

            initialized = true;

            try
            {
                ReadPreviousState();
            }
            catch (Exception ex)
            {
                SystemLogger.Log(
                    SystemLogLevel.Warning,
                    "BOOT",
                    "Previous boot-session marker unreadable: " + Safe(ex.Message));
            }

            MarkerAvailable = TryWriteState("running", "boot", Timestamp());

            if (HasPreviousSession && !PreviousSessionWasClean)
            {
                SystemLogger.Log(
                    SystemLogLevel.Warning,
                    "BOOT",
                    "Previous session did not record a clean shutdown. Last state reason=" +
                    PreviousExitReason + " time=" + PreviousTimestamp + ".");
            }
        }

        public static bool MarkCleanExit(string reason)
        {
            if (!initialized)
                Initialize();

            string safeReason = string.IsNullOrWhiteSpace(reason)
                ? "clean-exit"
                : SafeToken(reason, 48);

            bool ok = TryWriteState("clean", safeReason, Timestamp());
            if (ok)
            {
                SystemLogger.Log(
                    SystemLogLevel.Info,
                    "BOOT",
                    "Clean power transition recorded: " + safeReason + ".");
            }
            else
            {
                SystemLogger.Log(
                    SystemLogLevel.Warning,
                    "BOOT",
                    "Could not persist clean power transition: " + safeReason + ".");
            }

            return ok;
        }

        private static void ReadPreviousState()
        {
            HasPreviousSession = false;
            PreviousSessionWasClean = false;
            PreviousExitReason = "unknown";
            PreviousTimestamp = "unknown";

            if (!File.Exists(StatePath))
                return;

            FileInfo info = new FileInfo(StatePath);
            if (info.Length < 0 || info.Length > 4096)
                throw new InvalidDataException("boot-session marker size is invalid");

            string state = null;
            string reason = null;
            string time = null;

            using (StreamReader reader = new StreamReader(StatePath))
            {
                string line;
                int lines = 0;
                while ((line = reader.ReadLine()) != null && lines < 16)
                {
                    lines++;
                    int split = line.IndexOf('=');
                    if (split <= 0 || split >= line.Length - 1)
                        continue;

                    string key = line.Substring(0, split).Trim();
                    string value = line.Substring(split + 1).Trim();

                    if (key == "state")
                        state = value;
                    else if (key == "reason")
                        reason = value;
                    else if (key == "time")
                        time = value;
                }
            }

            if (string.IsNullOrEmpty(state))
                return;

            HasPreviousSession = true;
            PreviousSessionWasClean = string.Equals(state, "clean", StringComparison.Ordinal);
            PreviousExitReason = string.IsNullOrEmpty(reason) ? "unknown" : Safe(reason);
            PreviousTimestamp = string.IsNullOrEmpty(time) ? "unknown" : Safe(time);
        }

        private static bool TryWriteState(string state, string reason, string timestamp)
        {
            try
            {
                if (!Directory.Exists("/var/lib"))
                    Directory.CreateDirectory("/var/lib");
                if (!Directory.Exists(StateDirectory))
                    Directory.CreateDirectory(StateDirectory);

                string content =
                    "version=1\n" +
                    "state=" + state + "\n" +
                    "reason=" + SafeToken(reason, 48) + "\n" +
                    "time=" + Safe(timestamp) + "\n";

                if (File.Exists(TempPath))
                    File.Delete(TempPath);

                File.WriteAllText(TempPath, content);
                PermissionManager.SetPermission(TempPath, "root", 600);

                if (File.Exists(BackupPath))
                    File.Delete(BackupPath);

                if (File.Exists(StatePath))
                    File.Move(StatePath, BackupPath);

                try
                {
                    File.Move(TempPath, StatePath);
                }
                catch
                {
                    if (!File.Exists(StatePath) && File.Exists(BackupPath))
                        File.Move(BackupPath, StatePath);
                    throw;
                }

                PermissionManager.SetPermission(StatePath, "root", 600);

                if (File.Exists(BackupPath))
                    File.Delete(BackupPath);

                PermissionManager.RemovePermission(TempPath);
                PermissionManager.RemovePermission(BackupPath);
                return true;
            }
            catch
            {
                try
                {
                    if (File.Exists(TempPath))
                        File.Delete(TempPath);
                    PermissionManager.RemovePermission(TempPath);
                }
                catch { }

                return false;
            }
        }

        private static string Timestamp()
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

        private static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "unknown";

            string clean = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return clean.Length <= 96 ? clean : clean.Substring(0, 96);
        }

        private static string SafeToken(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            string input = value.Trim();
            string output = string.Empty;

            for (int i = 0; i < input.Length && output.Length < maxLength; i++)
            {
                char c = input[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
                    output += c;
                else if (c == ' ')
                    output += '-';
            }

            return output.Length == 0 ? "unknown" : output;
        }
    }

    /// <summary>
    /// Controlled power transitions for normal system operation. Panic/recovery
    /// paths intentionally call Cosmos Power directly so a failed boot is not
    /// mislabeled as a clean session.
    /// </summary>
    public static class SystemPower
    {
        public static void Reboot(string reason = "reboot")
        {
            BootSessionHealth.MarkCleanExit(reason);
            SystemLogger.Log(SystemLogLevel.Warning, "POWER", "Controlled reboot requested.");
            Cosmos.Kernel.System.Power.Reboot();
        }

        public static void Shutdown(string reason = "shutdown")
        {
            BootSessionHealth.MarkCleanExit(reason);
            SystemLogger.Log(SystemLogLevel.Warning, "POWER", "Controlled shutdown requested.");
            Cosmos.Kernel.System.Power.Shutdown();
        }
    }
}
