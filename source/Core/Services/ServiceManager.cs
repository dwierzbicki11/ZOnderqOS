using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cosmos.Kernel.Core.Memory;

namespace ZonderqOS.SystemCore.Services
{
    public sealed class ServiceStatus
    {
        public string Name { get; }
        public string Type { get; }
        public bool Enabled { get; }
        public bool Running { get; }
        public int Pid { get; }

        internal ServiceStatus(ServiceDefinition definition, bool running, int pid)
        {
            Name = definition.Name;
            Type = definition.Type;
            Enabled = definition.Enabled;
            Running = running;
            Pid = running ? pid : 0;
        }
    }

    public static class ServiceManager
    {
#if ZSRV_HOST_TESTS
        public static readonly string ConfigRoot = Path.Combine(Environment.GetEnvironmentVariable("ZSRV_TEST_ROOT"), "etc", "zservices");
        private static readonly string LogRoot = Path.Combine(Environment.GetEnvironmentVariable("ZSRV_TEST_ROOT"), "var", "log", "zservices");
#else
        public const string ConfigRoot = "/etc/zservices";
        private const string LogRoot = "/var/log/zservices";
#endif
        private const int MaxServices = 64;
        private static readonly object Sync = new object();
        private static readonly object ConfigWriteSync = new object();
        private static readonly Dictionary<string, ServiceDefinition> Definitions =
            new Dictionary<string, ServiceDefinition>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> Pids =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static void Initialize()
        {
            try
            {
                lock (ConfigWriteSync)
                {
                    Directory.CreateDirectory(ConfigRoot);
                    RecoverInterruptedConfigWrites();
                    if (!File.Exists(ConfigRoot + "/heartbeat.conf"))
                        File.WriteAllText(ConfigRoot + "/heartbeat.conf",
                            "# Edit and run: service reload\n" +
                            "type=heartbeat\n" +
                            "enabled=true\n" +
                            "interval_seconds=60\n");
                }

                string error;
                if (!Reload(out error))
                    SystemLogger.Log(SystemLogLevel.Warning, "SERVICE", error);
            }
            catch (Exception ex)
            {
                SystemLogger.Log(SystemLogLevel.Warning, "SERVICE", "Service setup failed: " + ex.Message);
            }
        }

        public static bool Reload(out string error)
        {
            error = string.Empty;
            var next = new Dictionary<string, ServiceDefinition>(StringComparer.OrdinalIgnoreCase);
            try
            {
                lock (ConfigWriteSync)
                {
                    if (!Directory.Exists(ConfigRoot))
                    {
                        error = "Service configuration directory does not exist.";
                        return false;
                    }

                    RecoverInterruptedConfigWrites();

                    string[] files = Directory.GetFiles(ConfigRoot);
                    for (int i = 0; i < files.Length; i++)
                    {
                        if (!files[i].EndsWith(".conf", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (next.Count >= MaxServices)
                        {
                            error = "Too many services (max 64).";
                            return false;
                        }

                        string name = Path.GetFileNameWithoutExtension(files[i]);
                        string parseError;
                        ServiceDefinition definition;
                        if (!ServiceDefinition.TryParse(name, ReadBounded(files[i]), out definition, out parseError))
                        {
                            error = "Invalid " + name + ".conf: " + parseError;
                            return false;
                        }
                        if (next.ContainsKey(name))
                        {
                            error = "Duplicate service name: " + name;
                            return false;
                        }
                        next.Add(name, definition);
                    }
                }
            }
            catch (Exception ex)
            {
                error = "Cannot load services: " + ex.Message;
                return false;
            }

            var pendingStart = new List<ServiceDefinition>();
            lock (Sync)
            {
                // Preserve the old valid configuration if even one file is bad.
                var keptRunning = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var existing in Definitions)
                {
                    ServiceDefinition replacement;
                    bool keepRunning = next.TryGetValue(existing.Key, out replacement) &&
                        replacement.Enabled &&
                        replacement.Type == existing.Value.Type &&
                        replacement.IntervalSeconds == existing.Value.IntervalSeconds;
                    if (IsRunningLocked(existing.Key))
                    {
                        if (keepRunning)
                            keptRunning.Add(existing.Key);
                        else if (Pids.TryGetValue(existing.Key, out int existingPid))
                            ProcessManager.Kill(existingPid);
                    }
                    else if (!keepRunning)
                    {
                        Pids.Remove(existing.Key);
                    }
                }

                Definitions.Clear();
                foreach (var item in next)
                    Definitions.Add(item.Key, item.Value);

                foreach (var item in next)
                {
                    if (!item.Value.Enabled || keptRunning.Contains(item.Key))
                        continue;

                    if (!IsRunningLocked(item.Key))
                    {
                        string startError;
                        if (!StartLocked(item.Value, out startError) && error.Length == 0)
                            error = startError;
                    }
                    else
                    {
                        pendingStart.Add(item.Value);
                    }
                }
            }

            // A cancelled service can still be finishing its current loop.
            // Wait outside Sync so status queries and control calls do not block during sleep.
            for (int p = 0; p < pendingStart.Count; p++)
            {
                ServiceDefinition definition = pendingStart[p];
                bool handled = false;
                for (int i = 0; i < 200; i++)
                {
                    Thread.Sleep(10);
                    lock (Sync)
                    {
                        ServiceDefinition current;
                        if (!Definitions.TryGetValue(definition.Name, out current) ||
                            !ReferenceEquals(current, definition) || !current.Enabled)
                        {
                            handled = true;
                            break;
                        }

                        if (!IsRunningLocked(definition.Name))
                        {
                            string startError;
                            if (!StartLocked(current, out startError) && error.Length == 0)
                                error = startError;
                            handled = true;
                            break;
                        }
                    }
                }

                if (!handled)
                {
                    lock (Sync)
                    {
                        ServiceDefinition current;
                        if (Definitions.TryGetValue(definition.Name, out current) &&
                            ReferenceEquals(current, definition) && current.Enabled)
                        {
                            if (!IsRunningLocked(definition.Name))
                            {
                                string startError;
                                if (!StartLocked(current, out startError) && error.Length == 0)
                                    error = startError;
                            }
                            else if (error.Length == 0)
                            {
                                error = "Service is still stopping: " + definition.Name;
                            }
                        }
                    }
                }
            }

            return string.IsNullOrEmpty(error);
        }

        public static bool Start(string name, out string error)
        {
            lock (Sync)
            {
                ServiceDefinition definition;
                if (!Definitions.TryGetValue(name ?? string.Empty, out definition))
                {
                    error = "Unknown service.";
                    return false;
                }
                return StartLocked(definition, out error);
            }
        }

        public static bool Stop(string name, out string error)
        {
            lock (Sync)
            {
                error = string.Empty;
                string key = name ?? string.Empty;
                int pid;
                if (!Definitions.ContainsKey(key) || !IsRunningLocked(key) || !Pids.TryGetValue(key, out pid))
                {
                    error = "Service is not running or does not exist.";
                    return false;
                }
                if (!ProcessManager.Kill(pid))
                {
                    error = "Service exited before stop request.";
                    return false;
                }
                return true;
            }
        }

        public static bool Restart(string name, out string error)
        {
            if (!ProcessManager.IsRunning("svc-" + name))
                return Start(name, out error);
            if (!Stop(name, out error))
                return false;

            // Cancellation is cooperative; never launch a second copy before
            // the original thread has actually left the process registry.
            for (int i = 0; i < 200; i++)
            {
                if (!ProcessManager.IsRunning("svc-" + name))
                    return Start(name, out error);
                Thread.Sleep(10);
            }
            error = "Service did not stop within two seconds.";
            return false;
        }

        public static bool SetEnabled(string name, bool enabled, out string error)
        {
            error = string.Empty;
            if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0)
            {
                error = "Only authenticated root can change service configuration.";
                return false;
            }
            if (!ServiceDefinition.IsSafeName(name))
            {
                error = "Invalid service name.";
                return false;
            }

            string canonicalName;
            lock (Sync)
            {
                ServiceDefinition definition;
                if (!Definitions.TryGetValue(name, out definition))
                {
                    error = "Unknown service.";
                    return false;
                }
                canonicalName = definition.Name;
            }

            string path = ConfigRoot + "/" + canonicalName + ".conf";
            string temp = path + ".new";
            string backup = path + ".bak";
            lock (ConfigWriteSync)
            {
                try
                {
                    string updated;
                    if (!ServiceDefinition.TrySetEnabled(canonicalName, ReadBounded(path), enabled,
                        out updated, out error))
                        return false;

                    File.WriteAllText(temp, updated);
                    if (File.Exists(backup))
                        File.Delete(backup);
                    File.Move(path, backup);
                    try
                    {
                        File.Move(temp, path);
                    }
                    catch
                    {
                        if (!File.Exists(path) && File.Exists(backup))
                            File.Move(backup, path);
                        throw;
                    }

                    try
                    {
                        if (File.Exists(backup))
                            File.Delete(backup);
                    }
                    catch (Exception ex)
                    {
                        SystemLogger.Log(SystemLogLevel.Warning, "SERVICE",
                            "Could not remove backup config '" + backup + "': " + ex.Message);
                    }

                    PermissionManager.SetPermission(path, "root", 644);
                }
                catch (Exception ex)
                {
                    error = "Could not update service: " + ex.Message;
                    return false;
                }
                finally
                {
                    try
                    {
                        if (File.Exists(temp))
                            File.Delete(temp);
                    }
                    catch { }
                }
            }

            return Reload(out error);
        }

        public static void FillStatus(List<ServiceStatus> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            lock (Sync)
            {
                destination.Clear();
                foreach (var item in Definitions)
                {
                    bool running = IsRunningLocked(item.Key);
                    int pid;
                    Pids.TryGetValue(item.Key, out pid);
                    destination.Add(new ServiceStatus(item.Value, running, pid));
                }
                destination.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
        }

        private static bool StartLocked(ServiceDefinition definition, out string error)
        {
            error = string.Empty;
            if (IsRunningLocked(definition.Name))
            {
                error = "Service is already running.";
                return false;
            }
            try
            {
                Directory.CreateDirectory(LogRoot);
                int pid = ProcessManager.Start("svc-" + definition.Name, token => RunService(definition, token));
                Pids[definition.Name] = pid;
                return true;
            }
            catch (Exception ex)
            {
                error = "Could not start " + definition.Name + ": " + ex.Message;
                return false;
            }
        }

        private static bool IsRunningLocked(string name)
        {
            return ProcessManager.IsRunning("svc-" + name);
        }

        private static void RunService(ServiceDefinition definition, CancellationToken token)
        {
            string log = LogRoot + "/" + definition.Name + ".log";
#if !ZSRV_HOST_TESTS
            bool wroteInitialHeartbeat = false;
#endif
            while (!token.IsCancellationRequested)
            {
                try
                {
                    bool skipDiskAppend = false;
#if !ZSRV_HOST_TESTS
                    // During idle desktop operation, appending service logs to FAT32 in a loop
                    // allocates FileStream/StreamWriter/DateTime.ToString objects and 32 KiB
                    // FAT32 cluster buffers continuously. Record the initial startup entry once
                    // and keep periodic service telemetry in RAM.
                    if (wroteInitialHeartbeat)
                        skipDiskAppend = true;
#endif
                    if (!skipDiskAppend)
                    {
                        string value = definition.Type == "memory"
                            ? "free_pages=" + PageAllocator.FreePageCount
                            : "heartbeat";
                        // Reuse the append handle for the length check instead of
                        // reopening FAT metadata through FileInfo after every write.
                        // The length is read after Flush, so partial/failed writes do
                        // not advance a separate cached counter.
                        bool rotate;
                        using (var stream = new FileStream(log, FileMode.Append, FileAccess.Write))
                        {
                            using (var writer = new StreamWriter(stream))
                            {
                                writer.Write(DateTime.UtcNow.ToString("u"));
                                writer.Write(" ");
                                writer.Write(value);
                                writer.Write("\n");
                                writer.Flush();
                                rotate = stream.Length > 64 * 1024;
                            }
                        }
                        // Close the append handle before truncating the log.
                        if (rotate)
                            File.WriteAllText(log, "service log rotated\n");
#if !ZSRV_HOST_TESTS
                        wroteInitialHeartbeat = true;
#endif
                    }
                }
                catch (Exception ex)
                {
                    SystemLogger.Log(SystemLogLevel.Warning, "SERVICE",
                        definition.Name + " log write failed: " + ex.Message);
                }

                if (token.WaitHandle.WaitOne(definition.IntervalSeconds * 1000))
                    break;
            }
        }

        private static string ReadBounded(string path)
        {
            char[] buffer = new char[2049];
            using (var reader = new StreamReader(path))
            {
                int count = 0;
                while (count < buffer.Length)
                {
                    int read = reader.Read(buffer, count, buffer.Length - count);
                    if (read == 0)
                        break;
                    count += read;
                }
                if (count > 2048)
                    throw new InvalidOperationException("Service configuration exceeds 2048 characters.");
                return new string(buffer, 0, count);
            }
        }

        private static void RecoverInterruptedConfigWrites()
        {
            if (!Directory.Exists(ConfigRoot))
                return;

            string[] files = Directory.GetFiles(ConfigRoot);
            for (int i = 0; i < files.Length; i++)
            {
                string backup = files[i];
                if (!backup.EndsWith(".conf.bak", StringComparison.OrdinalIgnoreCase))
                    continue;
                string name = Path.GetFileName(backup);
                name = name.Substring(0, name.Length - ".conf.bak".Length);
                if (!ServiceDefinition.IsSafeName(name))
                    continue;
                string original = ConfigRoot + "/" + name + ".conf";
                try
                {
                    if (!File.Exists(original))
                        File.Move(backup, original);
                    else
                        File.Delete(backup);
                }
                catch (Exception ex)
                {
                    SystemLogger.Log(SystemLogLevel.Warning, "SERVICE",
                        "Failed to recover backup config '" + backup + "': " + ex.Message);
                }
            }

            for (int i = 0; i < files.Length; i++)
            {
                string temp = files[i];
                if (!temp.EndsWith(".conf.new", StringComparison.OrdinalIgnoreCase))
                    continue;
                string name = Path.GetFileName(temp);
                name = name.Substring(0, name.Length - ".conf.new".Length);
                if (!ServiceDefinition.IsSafeName(name))
                    continue;
                try
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
                catch (Exception ex)
                {
                    SystemLogger.Log(SystemLogLevel.Warning, "SERVICE",
                        "Failed to clean stale temp config '" + temp + "': " + ex.Message);
                }
            }
        }
    }
}
