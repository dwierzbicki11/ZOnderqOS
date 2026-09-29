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
        private static readonly Dictionary<string, ServiceDefinition> Definitions =
            new Dictionary<string, ServiceDefinition>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> Pids =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static void Initialize()
        {
            try
            {
                Directory.CreateDirectory(ConfigRoot);
                RecoverInterruptedConfigWrites();
                if (!File.Exists(ConfigRoot + "/heartbeat.conf"))
                    File.WriteAllText(ConfigRoot + "/heartbeat.conf",
                        "# Edit and run: service reload\n" +
                        "type=heartbeat\n" +
                        "enabled=true\n" +
                        "interval_seconds=60\n");

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
            catch (Exception ex)
            {
                error = "Cannot load services: " + ex.Message;
                return false;
            }

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
                        else
                            ProcessManager.Kill(Pids[existing.Key]);
                    }
                }

                Definitions.Clear();
                foreach (var item in next)
                    Definitions.Add(item.Key, item.Value);

                foreach (var item in next)
                {
                    if (!item.Value.Enabled)
                        continue;
                    if (keptRunning.Contains(item.Key))
                        continue;

                    // A cancelled service can still be finishing its current loop.
                    // Wait for it to exit before starting the replacement.
                    for (int i = 0; i < 200 && IsRunningLocked(item.Key); i++)
                        Thread.Sleep(10);
                    if (!IsRunningLocked(item.Key))
                    {
                        string startError;
                        if (!StartLocked(item.Value, out startError) && error.Length == 0)
                            error = startError;
                    }
                    else if (error.Length == 0)
                        error = "Service is still stopping: " + item.Key;
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
                if (!Definitions.ContainsKey(name ?? string.Empty) || !IsRunningLocked(name))
                {
                    error = "Service is not running or does not exist.";
                    return false;
                }
                if (!ProcessManager.Kill(Pids[name]))
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

            lock (Sync)
            {
                ServiceDefinition definition;
                if (!Definitions.TryGetValue(name, out definition))
                {
                    error = "Unknown service.";
                    return false;
                }

                name = definition.Name;
                string path = ConfigRoot + "/" + name + ".conf";
                string temp = path + ".new";
                string backup = path + ".bak";
                try
                {
                    string updated;
                    if (!ServiceDefinition.TrySetEnabled(name, ReadBounded(path), enabled,
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
                        if (!File.Exists(path))
                            File.Move(backup, path);
                        throw;
                    }

                    PermissionManager.SetPermission(path, "root", 644);
                    return Reload(out error);
                }
                catch (Exception ex)
                {
                    error = "Could not update service: " + ex.Message;
                    return false;
                }
            }
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
            while (!token.IsCancellationRequested)
            {
                try
                {
                    string value = definition.Type == "memory"
                        ? "free_pages=" + PageAllocator.FreePageCount
                        : "heartbeat";
                    File.AppendAllText(log, DateTime.UtcNow.ToString("u") + " " + value + "\n");
                    // Prevent unlimited disk use on unattended boot sessions.
                    if (new FileInfo(log).Length > 64 * 1024)
                        File.WriteAllText(log, "service log rotated\n");
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
                if (!File.Exists(original))
                    File.Move(backup, original);
            }
        }
    }
}
