using System;
using System.Diagnostics;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class SystemLogApp : SettingsToolWindow
    {
        private const int MaxCachedLines = 64;
        private readonly string[] cachedLines = new string[MaxCachedLines];
        private int lineCount;
        private int scrollOffset;
        private int sourceMode; // 0 auth.log, 1 sysmon.log
        private long fileBytes;
        private long clearArmedAt;

        public SystemLogApp(int x, int y)
            : base("Dzienniki systemowe", "Logi i audyt - ZOnderqOS", x, y, 980, 660)
        {
            RefreshData();
        }

        private string CurrentPath
        {
            get { return sourceMode == 0 ? "/var/log/auth.log" : "/sysmon.log"; }
        }

        private string CurrentName
        {
            get { return sourceMode == 0 ? "AUTH / SECURITY" : "SYSTEM GUARDIAN"; }
        }

        protected override void RenderContent(Canvas canvas)
        {
            DrawRow(canvas, 0, IconType.File,
                "ZRODLO LOGU", "Kliknij aby przelaczyc auth.log / sysmon.log", CurrentName, Accent);
            DrawNumericRow(canvas, 1, IconType.File,
                "ROZMIAR PLIKU", CurrentPath, fileBytes > 0 ? (ulong)fileBytes / 1024UL : 0UL, " KB");

            int x = Window.X + 22;
            int y = RowY(2);
            int width = Window.Width - 44;
            int height = Window.Height - (y - Window.Y) - 58;
            if (height < 80) height = 80;
            canvas.DrawFilledRectangle(Color.FromArgb(18, 23, 29), x, y, width, height);
            canvas.DrawRectangle(Border, x, y, width, height);

            SmallTextRenderer.Draw(canvas, "PODGLAD OSTATNICH WPISOW", x + 12, y + 12, Muted);
            SmallTextRenderer.Draw(canvas, "STRZALKI GORA/DOL: PRZEWIJANIE  |  DELETE: WYCZYSC (2X)", x + 12, y + 27, Muted);
            canvas.DrawLine(Border, x + 10, y + 42, x + width - 10, y + 42);

            int visible = System.Math.Max(1, (height - 54) / 14);
            int start = scrollOffset;
            if (start < 0) start = 0;
            int maxStart = System.Math.Max(0, lineCount - visible);
            if (start > maxStart) start = maxStart;

            for (int i = 0; i < visible; i++)
            {
                int index = start + i;
                if (index >= lineCount)
                    break;
                SmallTextRenderer.DrawClipped(canvas, cachedLines[index], x + 12, y + 52 + i * 14,
                    System.Math.Max(20, width - 24), Text);
            }

            if (lineCount == 0)
                SmallTextRenderer.Draw(canvas, "BRAK WPISOW", x + 12, y + 56, Muted);
        }

        protected override void RefreshData()
        {
            for (int i = 0; i < cachedLines.Length; i++)
                cachedLines[i] = null;
            lineCount = 0;
            fileBytes = 0;

            try
            {
                string path = CurrentPath;
                if (!File.Exists(path))
                    return;

                fileBytes = new FileInfo(path).Length;
                string[] all = File.ReadAllLines(path);
                int start = System.Math.Max(0, all.Length - MaxCachedLines);
                for (int i = start; i < all.Length && lineCount < MaxCachedLines; i++)
                    cachedLines[lineCount++] = all[i];

                scrollOffset = System.Math.Max(0, lineCount - 20);
            }
            catch
            {
                lineCount = 0;
                fileBytes = 0;
            }
        }

        protected override void OnClick(int mouseX, int mouseY)
        {
            int row = HitRow(mouseX, mouseY, 2);
            if (row == 0)
            {
                sourceMode = sourceMode == 0 ? 1 : 0;
                clearArmedAt = 0;
                RefreshData();
                SetStatus("ZMIENIONO ZRODLO LOGU", Accent);
            }
            else if (row == 1)
            {
                RefreshData();
                SetStatus("LOG ODSWIEZONY", Good);
            }
        }

        protected override void OnKeyboard(KeyEvent key)
        {
            if (key.Key == ConsoleKeyEx.UpArrow)
            {
                if (scrollOffset > 0)
                    scrollOffset--;
            }
            else if (key.Key == ConsoleKeyEx.DownArrow)
            {
                if (scrollOffset + 1 < lineCount)
                    scrollOffset++;
            }
            else if (key.Key == ConsoleKeyEx.LeftArrow || key.Key == ConsoleKeyEx.RightArrow)
            {
                sourceMode = sourceMode == 0 ? 1 : 0;
                clearArmedAt = 0;
                RefreshData();
                SetStatus("ZMIENIONO ZRODLO LOGU", Accent);
            }
            else if (key.Key == ConsoleKeyEx.Delete)
            {
                RequestClear();
            }
        }

        private void RequestClear()
        {
            if (global::ZonderqOS.SecurityContext.CurrentUser != "root")
            {
                SetStatus("TYLKO ROOT MOZE CZYSIC LOGI", Danger);
                return;
            }

            long now = Stopwatch.GetTimestamp();
            long frequency = Stopwatch.Frequency;
            bool armed = clearArmedAt > 0 && frequency > 0 && now >= clearArmedAt &&
                         now - clearArmedAt <= frequency * 6L;
            if (!armed)
            {
                clearArmedAt = now;
                SetStatus("DELETE PONOWNIE W CIAGU 6 S ABY WYCZYSC LOG", Warning);
                return;
            }

            clearArmedAt = 0;
            try
            {
                File.WriteAllText(CurrentPath, string.Empty);
                global::ZonderqOS.SecurityLogger.LogEvent("WARN", "System log cleared from Settings GUI.");
                RefreshData();
                SetStatus("LOG WYCZYSZCZONY", Warning);
            }
            catch
            {
                SetStatus("NIE UDALO SIE WYCZYSCIC LOGU", Danger);
            }
        }
    }
}
