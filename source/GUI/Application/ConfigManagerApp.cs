using System;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;
using ZonderqOS.SystemCore;
using ZonderqOS.SystemCore.Services;

namespace ZonderqOS.GUI.Apps
{
    public sealed class ConfigManagerApp : SettingsToolWindow
    {
        private readonly Action<string> openEditor;
        private readonly string[] serviceFiles = new string[64];
        private int serviceCount;
        private int selectedService;
        private string hostname = "?";

        public ConfigManagerApp(int x, int y, Action<string> openEditor)
            : base("Configuration /etc", "Configuration files - ZonderqOS", x, y, 920, 650)
        {
            this.openEditor = openEditor;
            RefreshData();
        }

        protected override void RenderContent(Canvas canvas)
        {
            DrawRow(canvas, 0, IconType.Settings, "HOSTNAME", "/etc/hostname - computer name", hostname, Accent);
            DrawRow(canvas, 1, IconType.File, "ISSUE", "/etc/issue - login banner", "OPEN", Text);
            DrawRow(canvas, 2, IconType.File, "MOTD", "/etc/motd - login message", "OPEN", Text);
            DrawRow(canvas, 3, IconType.File, "PROFILE", "/etc/profile - shell variables", "OPEN", Text);
            DrawRow(canvas, 4, IconType.File, "OS RELEASE", "/etc/os-release - system information", "OPEN", Text);
            DrawRow(canvas, 5, IconType.Settings, "SERVICES", "/etc/zservices - use left/right arrows to select a file",
                serviceCount > 0 ? Path.GetFileName(serviceFiles[selectedService]) : "NONE", Warning);
            DrawRow(canvas, 6, IconType.Settings, "APPLY", "Hostname, profile and services (root required)",
                "RELOAD", Good);
        }

        protected override void RefreshData()
        {
            hostname = global::ZonderqOS.EnvironmentManager.Get("HOSTNAME");
            serviceCount = 0;
            selectedService = 0;
            try
            {
                if (!Directory.Exists(ServiceManager.ConfigRoot))
                    return;
                string[] files = Directory.GetFiles(ServiceManager.ConfigRoot);
                for (int i = 0; i < files.Length && serviceCount < serviceFiles.Length; i++)
                    if (files[i].EndsWith(".conf", StringComparison.OrdinalIgnoreCase))
                        serviceFiles[serviceCount++] = files[i];
                Array.Sort(serviceFiles, 0, serviceCount, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                SetStatus("CANNOT READ SERVICES DIRECTORY", Danger);
            }
        }

        protected override void OnClick(int mouseX, int mouseY)
        {
            int row = HitRow(mouseX, mouseY, 7);
            if (row == 6)
            {
                Apply();
                return;
            }

            string path = row == 0 ? "/etc/hostname" :
                row == 1 ? "/etc/issue" :
                row == 2 ? "/etc/motd" :
                row == 3 ? "/etc/profile" :
                row == 4 ? "/etc/os-release" :
                row == 5 && serviceCount > 0 ? serviceFiles[selectedService] : null;
            if (path == null)
                return;

            if (!global::ZonderqOS.PermissionManager.CanRead(path,
                global::ZonderqOS.SecurityContext.CurrentUser))
            {
                SetStatus("READ PERMISSION DENIED", Danger);
                return;
            }

            openEditor?.Invoke(path);
            SetStatus("OTWARTO " + path, Accent);
        }

        protected override void OnKeyboard(KeyEvent key)
        {
            if (serviceCount > 0 && key.Key == ConsoleKeyEx.LeftArrow)
                selectedService = (selectedService + serviceCount - 1) % serviceCount;
            else if (serviceCount > 0 && key.Key == ConsoleKeyEx.RightArrow)
                selectedService = (selectedService + 1) % serviceCount;
        }

        private void Apply()
        {
            if (!global::ZonderqOS.SecurityContext.IsAuthenticated ||
                global::ZonderqOS.SecurityContext.CurrentUid != 0)
            {
                SetStatus("ONLY ROOT CAN APPLY SETTINGS", Danger);
                return;
            }

            string error;
            global::ZonderqOS.EnvironmentManager.LoadProfile();
            if (!SystemIdentity.ReloadHostname(out error))
            {
                SetStatus(error, Danger);
                return;
            }
            if (!ServiceManager.Reload(out error))
            {
                SetStatus(error, Danger);
                return;
            }
            RefreshData();
            SetStatus("CONFIGURATION APPLIED", Good);
        }
    }
}
