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
        public const string ConfigRoot = "/etc/zservices";
        private const string LogRoot = "/var/log/zservices";
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
                foreach (var existing in Definitions)
                {
                    ServiceDefinition replacement;
                    if ((!next.TryGetValue(existing.Key, out replacement) || !replacement.Enabled) &&
                        IsRunningLocked(existing.Key))
                        ProcessManager.Kill(Pids[existing.Key]);
                }

                Definitions.Clear();
                foreach (var item in next)
                    Definitions.Add(item.Key, item.Value);

                foreach (var item in next)
                    if (item.Value.Enabled && !IsRunningLocked(item.Key))
                        StartLocked(item.Value, out error);
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
    }
}
