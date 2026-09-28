using System;

namespace ZonderqOS.SystemCore.Services
{
    public sealed class ServiceDefinition
    {
        public string Name { get; }
        public string Type { get; }
        public bool Enabled { get; }
        public int IntervalSeconds { get; }

        private ServiceDefinition(string name, string type, bool enabled, int intervalSeconds)
        {
            Name = name;
            Type = type;
            Enabled = enabled;
            IntervalSeconds = intervalSeconds;
        }

        public static bool TryParse(string name, string content, out ServiceDefinition definition, out string error)
        {
            definition = null;
            error = string.Empty;
            if (!IsSafeName(name) || content == null || content.Length > 2048)
            {
                error = "Invalid service name or oversized configuration.";
                return false;
            }

            string type = null;
            bool enabled = false;
            int interval = 60;
            bool seenEnabled = false;
            bool seenInterval = false;
            string[] lines = content.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                int separator = line.IndexOf('=');
                if (separator < 1)
                {
                    error = "Invalid line " + (i + 1) + ".";
                    return false;
                }

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                if (key == "type" && type == null)
                    type = value;
                else if (key == "enabled" && !seenEnabled)
                {
                    seenEnabled = true;
                    if (!bool.TryParse(value, out enabled))
                    {
                        error = "enabled must be true or false.";
                        return false;
                    }
                }
                else if (key == "interval_seconds" && !seenInterval)
                {
                    seenInterval = true;
                    if (!int.TryParse(value, out interval) || interval < 5 || interval > 3600)
                    {
                        error = "interval_seconds must be between 5 and 3600.";
                        return false;
                    }
                }
                else
                {
                    error = "Unknown or duplicate service setting: " + key;
                    return false;
                }
            }

            if (type != "heartbeat" && type != "memory")
            {
                error = "type must be heartbeat or memory.";
                return false;
            }

            definition = new ServiceDefinition(name, type, enabled, interval);
            return true;
        }

        public static bool IsSafeName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 32 ||
                !IsAsciiLetterOrDigit(name[0]))
                return false;

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (!IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
                    return false;
            }
            return true;
        }

        private static bool IsAsciiLetterOrDigit(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                   (c >= '0' && c <= '9');
        }
    }
}
