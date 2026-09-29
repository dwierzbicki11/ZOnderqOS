using System;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
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
            : base("Konfiguracja /etc", "Pliki konfiguracyjne - ZonderqOS", x, y, 920, 650)
        {
            this.openEditor = openEditor;
            RefreshData();
        }

        protected override void RenderContent(Canvas canvas)
        {
            DrawRow(canvas, 0, IconType.Settings, "HOSTNAME", "/etc/hostname - nazwa komputera", hostname, Accent);
            DrawRow(canvas, 1, IconType.File, "ISSUE", "/etc/issue - ekran logowania", "OTWORZ", Text);
            DrawRow(canvas, 2, IconType.File, "MOTD", "/etc/motd - komunikat po logowaniu", "OTWORZ", Text);
            DrawRow(canvas, 3, IconType.File, "PROFILE", "/etc/profile - zmienne powloki", "OTWORZ", Text);
            DrawRow(canvas, 4, IconType.File, "OS RELEASE", "/etc/os-release - informacje o systemie", "OTWORZ", Text);
            DrawRow(canvas, 5, IconType.Settings, "USLUGI", "/etc/zservices - strzalki lewo/prawo zmieniaja plik",
                serviceCount > 0 ? Path.GetFileName(serviceFiles[selectedService]) : "BRAK", Warning);
            DrawRow(canvas, 6, IconType.Settings, "ZASTOSUJ", "Hostname, profil i uslugi (wymaga root)",
                "WCZYTAJ", Good);
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
                SetStatus("NIE MOZNA ODCZYTAC KATALOGU USLUG", Danger);
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
                SetStatus("BRAK UPRAWNIEN DO ODCZYTU", Danger);
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
                SetStatus("TYLKO ROOT MOZE ZASTOSOWAC USTAWIENIA", Danger);
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
            SetStatus("KONFIGURACJA ZASTOSOWANA", Good);
        }
    }
}
