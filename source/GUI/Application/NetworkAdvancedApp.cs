using System;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Network.Config;
using Cosmos.Kernel.System.Network.IPv4;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class NetworkAdvancedApp : SettingsToolWindow
    {
        private string staticIp;
        private string staticMask;
        private string staticGateway;
        private string dnsServer;
        private string currentIp = "0.0.0.0";
        private string currentInterface = "BRAK";

        // 0 = no edit, 1 = IP, 2 = mask, 3 = gateway, 4 = DNS.
        private int editField;
        private string editText = string.Empty;

        public NetworkAdvancedApp(int x, int y)
            : base("Siec zaawansowana", "Zaawansowana konfiguracja sieci - ZOnderqOS", x, y, 920, 620)
        {
            global::ZonderqOS.SystemSettings.Load();
            staticIp = global::ZonderqOS.SystemSettings.StaticIpAddress;
            staticMask = global::ZonderqOS.SystemSettings.StaticSubnetMask;
            staticGateway = global::ZonderqOS.SystemSettings.StaticGateway;
            dnsServer = global::ZonderqOS.SystemSettings.DnsServer;
            RefreshData();
        }

        protected override void RenderContent(Canvas canvas)
        {
            DrawRow(canvas, 0, IconType.Settings,
                "TRYB KONFIGURACJI", "Kliknij aby przelaczyc i zastosowac DHCP lub statyczne IPv4",
                global::ZonderqOS.SystemSettings.NetworkModeName,
                global::ZonderqOS.SystemSettings.NetworkUseDhcp ? Good : Accent);

            DrawEditRow(canvas, 1, "ADRES IPv4", "Statyczny adres interfejsu", staticIp, 1);
            DrawEditRow(canvas, 2, "MASKA PODSIECI", "Maska IPv4, np. 255.255.255.0", staticMask, 2);
            DrawEditRow(canvas, 3, "BRAMA DOMYSLNA", "Adres routera / gateway", staticGateway, 3);
            DrawEditRow(canvas, 4, "SERWER DNS", "Preferowany resolver DNS", dnsServer, 4);

            DrawRow(canvas, 5, IconType.Refresh,
                "ZASTOSUJ STATYCZNE", "Waliduj pola, skonfiguruj interfejs i zapisz profil",
                "ZASTOSUJ", Warning);

            DrawRow(canvas, 6, IconType.Settings,
                "AKTYWNE POLACZENIE", currentInterface, currentIp,
                global::ZonderqOS.Network.IsReady ? Good : Danger);
        }

        private void DrawEditRow(Canvas canvas, int row, string title, string description, string value, int field)
        {
            bool editing = editField == field;
            DrawRow(canvas, row, IconType.Settings, title, description,
                editing ? editText : value, editing ? Warning : Text);

            if (editing)
            {
                int x = Window.X + Window.Width - 37;
                int y = RowY(row) + 24;
                canvas.DrawFilledRectangle(Accent, x, y, 2, 13);
            }
        }

        protected override void RefreshData()
        {
            try
            {
                var active = global::ZonderqOS.Network.ActiveDevice;
                currentInterface = active != null && !string.IsNullOrEmpty(active.Name) ? active.Name : "BRAK";
                Address address = NetworkConfigManager.CurrentAddress;
                currentIp = address != null ? address.ToString() : "0.0.0.0";
            }
            catch
            {
                currentInterface = "BRAK";
                currentIp = "0.0.0.0";
            }
        }

        protected override void OnClick(int mouseX, int mouseY)
        {
            int row = HitRow(mouseX, mouseY, 7);
            if (row < 0)
                return;

            if (row == 0)
            {
                CancelEdit();
                if (global::ZonderqOS.SystemSettings.NetworkUseDhcp)
                {
                    ApplyStatic();
                }
                else
                {
                    global::ZonderqOS.SystemSettings.SetNetworkModeDhcp(true);
                    bool ok = global::ZonderqOS.Network.ConfigureDhcp();
                    RefreshData();
                    SetStatus(ok ? "WLACZONO DHCP" : "DHCP NIE UZYSKAL KONFIGURACJI", ok ? Good : Danger);
                }
                return;
            }

            if (row >= 1 && row <= 4)
            {
                BeginEdit(row);
                return;
            }

            if (row == 5)
            {
                ApplyStatic();
                return;
            }

            if (row == 6)
            {
                RefreshData();
                SetStatus("ODSWIEZONO STAN POLACZENIA", Good);
            }
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key == null)
                return;

            if (editField == 0)
            {
                base.HandleKeyboard(key);
                return;
            }

            if (key.Key == ConsoleKeyEx.Escape)
            {
                CancelEdit();
                SetStatus("ANULOWANO EDYCJE", Muted);
                return;
            }

            if (key.Key == ConsoleKeyEx.Enter)
            {
                CommitEdit();
                return;
            }

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                if (editText.Length > 0)
                    editText = editText.Substring(0, editText.Length - 1);
                return;
            }

            char ch = key.KeyChar;
            if (((ch >= '0' && ch <= '9') || ch == '.') && editText.Length < 15)
                editText += ch;
        }

        private void BeginEdit(int field)
        {
            editField = field;
            switch (field)
            {
                case 1: editText = staticIp; break;
                case 2: editText = staticMask; break;
                case 3: editText = staticGateway; break;
                case 4: editText = dnsServer; break;
                default: editText = string.Empty; break;
            }
            SetStatus("EDYCJA: ENTER ZAPISUJE POLE, ESC ANULUJE", Warning);
        }

        private void CommitEdit()
        {
            if (!IsValidAddress(editText))
            {
                SetStatus("NIEPRAWIDLOWY ADRES IPv4", Danger);
                return;
            }

            switch (editField)
            {
                case 1: staticIp = editText; break;
                case 2: staticMask = editText; break;
                case 3: staticGateway = editText; break;
                case 4: dnsServer = editText; break;
            }
            editField = 0;
            editText = string.Empty;
            SetStatus("POLE ZAKTUALIZOWANE - KLIKNIJ ZASTOSUJ", Good);
        }

        private void CancelEdit()
        {
            editField = 0;
            editText = string.Empty;
        }

        private void ApplyStatic()
        {
            CancelEdit();
            if (!IsValidAddress(staticIp) || !IsValidAddress(staticMask) ||
                !IsValidAddress(staticGateway) || !IsValidAddress(dnsServer))
            {
                SetStatus("SPRAWDZ IP, MASKE, BRAME I DNS", Danger);
                return;
            }

            SetStatus("KONFIGUROWANIE STATYCZNEGO IPv4...", Warning);
            bool ok = false;
            try
            {
                ok = global::ZonderqOS.Network.ConfigureStatic(staticIp, staticMask, staticGateway, dnsServer);
                if (ok)
                    global::ZonderqOS.SystemSettings.SetStaticNetwork(staticIp, staticMask, staticGateway, dnsServer);
            }
            catch
            {
                ok = false;
            }

            RefreshData();
            SetStatus(ok ? "STATYCZNE IPv4 I DNS ZASTOSOWANE" : "NIE UDALO SIE ZASTOSOWAC IPv4",
                ok ? Good : Danger);
        }

        private static bool IsValidAddress(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 15)
                return false;
            try
            {
                return Address.Parse(value) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
