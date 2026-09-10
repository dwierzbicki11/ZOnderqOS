using System;
using System.Collections.Generic;
using System.IO;

namespace ZonderqOS
{
    public static class EnvironmentManager
    {
        private const long MaxProfileBytes = 64 * 1024;
        private const int MaxVariables = 128;
        private const int MaxKeyLength = 64;
        private const int MaxValueLength = 4096;

        private static readonly Dictionary<string, string> _vars = new Dictionary<string, string>();
        private static readonly object _varsLock = new object();
        private static string ProfilePath = @"/etc/profile";

        public static void Initialize()
        {
            lock (_varsLock)
            {
                _vars.Clear();
                _vars["USER"] = "root";
                _vars["HOME"] = "/root";
                _vars["HOSTNAME"] = "ZonderqOS";
                _vars["PATH"] = "/bin";
            }

            try
            {
                if (!Directory.Exists("/etc"))
                    Directory.CreateDirectory("/etc");

                if (!File.Exists(ProfilePath))
                {
                    const string defaultProfile = "export USER=root\nexport HOME=/root\nexport HOSTNAME=ZonderqOS\nexport PATH=/bin\n";
                    File.WriteAllText(ProfilePath, defaultProfile);
                }
                else
                {
                    LoadProfile();
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Environment init error: {ex.Message}", "ENV");
            }
        }

        public static void Set(string key, string value)
        {
            TrySet(key, value);
        }

        public static bool TrySet(string key, string value)
        {
            if (!IsValidKey(key) || value == null || value.Length > MaxValueLength)
                return false;

            lock (_varsLock)
            {
                if (!_vars.ContainsKey(key) && _vars.Count >= MaxVariables)
                    return false;

                _vars[key] = value;
                return true;
            }
        }

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            lock (_varsLock)
            {
                return _vars.TryGetValue(key, out string value) ? value : string.Empty;
            }
        }

        public static void LoadProfile()
        {
            try
            {
                if (!File.Exists(ProfilePath))
                    return;

                FileInfo profileInfo = new FileInfo(ProfilePath);
                if (profileInfo.Length > MaxProfileBytes)
                {
                    WriteMessage.WriteError($"Profile is too large. Limit: {MaxProfileBytes / 1024} KB.", "ENV");
                    return;
                }

                using (var reader = new StreamReader(ProfilePath))
                {
                    string line;
                    int parsedLines = 0;
                    while ((line = reader.ReadLine()) != null && parsedLines < MaxVariables)
                    {
                        string trimmed = line.Trim();
                        if (!trimmed.StartsWith("export "))
                            continue;

                        string assignment = trimmed.Substring(7);
                        int separator = assignment.IndexOf('=');
                        if (separator <= 0)
                            continue;

                        string key = assignment.Substring(0, separator).Trim();
                        string value = assignment.Substring(separator + 1).Trim();
                        TrySet(key, value);
                        parsedLines++;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"LoadProfile error: {ex.Message}", "ENV");
            }
        }

        private static bool IsValidKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength)
                return false;

            char first = key[0];
            if (!(char.IsLetter(first) || first == '_'))
                return false;

            for (int i = 1; i < key.Length; i++)
            {
                char c = key[i];
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                    return false;
            }

            return true;
        }
    }
}
