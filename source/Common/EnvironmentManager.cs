using System;
using System.Collections.Generic;
using System.IO;

namespace ZonderqOS
{
    public static class EnvironmentManager
    {
        private static Dictionary<string, string> _vars = new Dictionary<string, string>();
        private static string ProfilePath = @"/etc/profile";

        public static void Initialize()
        {
            // Domyślne zmienne systemowe
            _vars["USER"] = "root";
            _vars["HOME"] = "/root";
            _vars["HOSTNAME"] = "ZonderqOS";
            _vars["PATH"] = "/bin";

            try
            {
                if (!Directory.Exists("/etc")) Directory.CreateDirectory("/etc");

                if (!File.Exists(ProfilePath))
                {
                    string defaultProfile = "export USER=root\nexport HOME=/root\nexport HOSTNAME=ZonderqOS\nexport PATH=/bin\n";
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
            if (_vars.ContainsKey(key))
                _vars[key] = value;
            else
                _vars.Add(key, value);
        }

        public static string Get(string key)
        {
            if (_vars.ContainsKey(key)) return _vars[key];
            return string.Empty;
        }

        public static void LoadProfile()
        {
            try
            {
                if (!File.Exists(ProfilePath)) return;
                string[] lines = File.ReadAllLines(ProfilePath);
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("export "))
                    {
                        string assignment = trimmed.Substring(7);
                        string[] kv = assignment.Split('=');
                        if (kv.Length == 2)
                        {
                            _vars[kv[0].Trim()] = kv[1].Trim();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"LoadProfile error: {ex.Message}", "ENV");
            }
        }
    }
}