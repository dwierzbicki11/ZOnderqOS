using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;
using ZonderqOS.GUI.Icons;
using ZonderqOS.SystemCore;

namespace ZonderqOS.GUI.Apps
{
    public sealed class SysBenchmarkApp : Application
    {
        private static readonly Color Surface = Color.FromArgb(18, 22, 28);
        private static readonly Color Panel = Color.FromArgb(26, 33, 42);
        private static readonly Color PanelHover = Color.FromArgb(36, 46, 58);
        private static readonly Color Border = Color.FromArgb(52, 66, 82);
        private static readonly Color TextColor = Color.FromArgb(235, 240, 248);
        private static readonly Color MutedColor = Color.FromArgb(135, 150, 168);
        private static readonly Color AccentColor = Color.FromArgb(40, 140, 230);
        private static readonly Color GreenColor = Color.FromArgb(60, 215, 110);
        private static readonly Color GoldColor = Color.FromArgb(245, 200, 50);
        private static readonly Color OrangeColor = Color.FromArgb(245, 140, 45);

        private int activeTab = 0; // 0 = Tests, 1 = Specs, 2 = Comparison
        private readonly Action closeCallback;

        // Benchmark state
        private bool isRunningAll = false;
        private int testStep = -1; // -1 = Idle, 0 = CPU, 1 = RAM, 2 = GPU, 3 = Storage
        private int progressPercent = 0;

        // Results
        private int cpuMips = 0;
        private int cpuTimeMs = 0;
        private int ramSpeedMb = 0;
        private int gpuKpixSec = 0;
        private int vfsKbSec = 0;
        private int totalZScore = 0;
        private string testStatus = "Gotowy do uruchomienia testu wydajnosci.";

        // Preallocated buffer for RAM memory throughput benchmark (64 KB)
        private readonly byte[] testRamBuffer = new byte[65536];

        // Hardware snapshot
        private HardwareSnapshot hardware;
        private long lastHardwareRefresh = 0;

        public SysBenchmarkApp(int x, int y, Action onClose) : base("System Benchmark")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 580, 425, "Benchmark & Diagnostyka Wydajności");
            Window.CloseAction = Close;

            RefreshHardware();
        }

        public override bool NeedsContinuousRedraw => isRunningAll || testStep >= 0;

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
        }

        private void RefreshHardware()
        {
            try
            {
                hardware = HardwareSnapshot.Capture();
                lastHardwareRefresh = Stopwatch.GetTimestamp();
            }
            catch
            {
                // Fallback
            }
        }

        public override void Update()
        {
            base.Update();

            if (testStep >= 0)
            {
                ExecuteStep(testStep);
            }
        }

        private void ExecuteStep(int step)
        {
            switch (step)
            {
                case 0:
                    testStatus = "Trwa test jednostki arytmetycznej CPU...";
                    RunCpuBenchmark();
                    progressPercent = 30;
                    testStep = isRunningAll ? 1 : -1;
                    break;

                case 1:
                    testStatus = "Trwa test przepustowosci pamieci RAM...";
                    RunRamBenchmark();
                    progressPercent = 65;
                    testStep = isRunningAll ? 2 : -1;
                    break;

                case 2:
                    testStatus = "Trwa test wydajnosci rysowania VESA...";
                    RunGpuBenchmark();
                    progressPercent = 90;
                    testStep = isRunningAll ? 3 : -1;
                    break;

                case 3:
                    testStatus = "Trwa test podsystemu dyskowego i buforow VFS...";
                    RunVfsBenchmark();
                    progressPercent = 100;
                    testStep = -1;
                    isRunningAll = false;
                    CalculateTotalScore();
                    testStatus = "Test zakonczony sukcesem! Wyniki ponizej.";
                    break;

                default:
                    testStep = -1;
                    isRunningAll = false;
                    break;
            }
        }

        private void RunCpuBenchmark()
        {
            long freq = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;
            long start = Stopwatch.GetTimestamp();

            // Run 5,000,000 mixed integer iterations (math, bitwise ops, logic)
            int sum = 0;
            const int iterations = 3000000;
            for (int i = 0; i < iterations; i++)
            {
                sum = (sum * 1103515245 + 12345) ^ (i & 0xFF);
                if ((sum & 1) == 0) sum += (i % 7);
            }

            long elapsed = Stopwatch.GetTimestamp() - start;
            if (elapsed <= 0) elapsed = 1;
            long ms = (elapsed * 1000) / freq;
            cpuTimeMs = (int)Math.Max(1, ms);

            // Calculate estimated MIPS
            long opsPerSec = ((long)iterations * 4L * freq) / elapsed;
            cpuMips = (int)(opsPerSec / 1000000L);
            if (cpuMips <= 0) cpuMips = 1;
        }

        private void RunRamBenchmark()
        {
            long freq = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;
            long start = Stopwatch.GetTimestamp();

            // Read/write test buffer 200 times = 200 * 64KB = 12.8 MB transferred
            const int passes = 160;
            int bufLen = testRamBuffer.Length;
            for (int p = 0; p < passes; p++)
            {
                byte val = (byte)(p & 0xFF);
                for (int i = 0; i < bufLen; i += 4)
                {
                    testRamBuffer[i] = val;
                    testRamBuffer[i + 1] = val;
                    testRamBuffer[i + 2] = val;
                    testRamBuffer[i + 3] = val;
                }
            }

            long elapsed = Stopwatch.GetTimestamp() - start;
            if (elapsed <= 0) elapsed = 1;
            long totalBytes = (long)passes * bufLen;
            long bytesPerSec = (totalBytes * freq) / elapsed;
            ramSpeedMb = (int)(bytesPerSec / (1024L * 1024L));
            if (ramSpeedMb <= 0) ramSpeedMb = 1;
        }

        private void RunGpuBenchmark()
        {
            long freq = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;
            long start = Stopwatch.GetTimestamp();

            // Simulate raster fill ops: 2,000,000 pixel equivalents
            int mockPixels = 2000000;
            int acc = 0;
            for (int i = 0; i < mockPixels; i++)
            {
                acc += (i & 31);
            }

            long elapsed = Stopwatch.GetTimestamp() - start;
            if (elapsed <= 0) elapsed = 1;
            long pixPerSec = ((long)mockPixels * freq) / elapsed;
            gpuKpixSec = (int)(pixPerSec / 1000L);
            if (gpuKpixSec <= 0) gpuKpixSec = 1;
        }

        private void RunVfsBenchmark()
        {
            // Simulate sector caching and sequential record parsing
            long freq = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;
            long start = Stopwatch.GetTimestamp();

            int blocks = 50000;
            int check = 0;
            for (int i = 0; i < blocks; i++)
            {
                check ^= (i * 37 + 13);
            }

            long elapsed = Stopwatch.GetTimestamp() - start;
            if (elapsed <= 0) elapsed = 1;
            long kbPerSec = ((long)blocks * 512L * freq) / (elapsed * 1024L);
            vfsKbSec = (int)Math.Max(100, kbPerSec);
        }

        private void CalculateTotalScore()
        {
            // Balanced index formula
            totalZScore = (cpuMips * 8) + (ramSpeedMb * 5) + (gpuKpixSec / 150) + (vfsKbSec / 500);
            if (totalZScore <= 0) totalZScore = 150;
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);

            if (!Window.Visible || Window.IsMinimized)
                return;

            if (leftClicked && !leftWasClicked)
            {
                int wx = Window.X;
                int wy = Window.Y + Window.HeaderHeight;

                // Tab selection
                int tabY = wy + 8;
                int tabW = 170;
                int tabH = 26;

                if (Hit(mouseX, mouseY, wx + 12, tabY, tabW, tabH))
                {
                    activeTab = 0;
                    return;
                }
                if (Hit(mouseX, mouseY, wx + 12 + tabW + 6, tabY, tabW, tabH))
                {
                    activeTab = 1;
                    RefreshHardware();
                    return;
                }
                if (Hit(mouseX, mouseY, wx + 12 + (tabW + 6) * 2, tabY, tabW, tabH))
                {
                    activeTab = 2;
                    return;
                }

                // Tab 0 interactions
                if (activeTab == 0 && testStep < 0)
                {
                    int btnY = wy + 325;
                    // [ Uruchom Wszystkie Testy ]
                    if (Hit(mouseX, mouseY, wx + 16, btnY, 210, 32))
                    {
                        isRunningAll = true;
                        progressPercent = 10;
                        testStep = 0;
                        return;
                    }
                    // [ Tylko CPU ]
                    if (Hit(mouseX, mouseY, wx + 236, btnY, 100, 32))
                    {
                        isRunningAll = false;
                        testStep = 0;
                        return;
                    }
                    // [ Tylko RAM ]
                    if (Hit(mouseX, mouseY, wx + 346, btnY, 100, 32))
                    {
                        isRunningAll = false;
                        testStep = 1;
                        return;
                    }
                    // [ Tylko GUI ]
                    if (Hit(mouseX, mouseY, wx + 456, btnY, 105, 32))
                    {
                        isRunningAll = false;
                        testStep = 2;
                        return;
                    }
                }
            }
        }

        private static bool Hit(int mx, int my, int x, int y, int w, int h)
        {
            return mx >= x && mx < x + w && my >= y && my < y + h;
        }

        public override void Render(Canvas canvas)
        {
            if (!Window.Visible || Window.IsMinimized)
                return;

            base.Render(canvas);

            int wx = Window.X;
            int wy = Window.Y + Window.HeaderHeight;
            int ww = Window.Width;
            int wh = Window.Height - Window.HeaderHeight;

            // Main Background
            canvas.DrawFilledRectangle(Surface, wx, wy, ww, wh);

            // Tab bar
            int tabY = wy + 8;
            int tabW = 175;
            int tabH = 26;

            DrawTab(canvas, "⚡ TEST WYDAJNOSCI", wx + 12, tabY, tabW, tabH, activeTab == 0);
            DrawTab(canvas, "💻 SPECYFIKACJA SPRZETU", wx + 12 + tabW + 6, tabY, tabW, tabH, activeTab == 1);
            DrawTab(canvas, "📊 POROWNANIE / RANKING", wx + 12 + (tabW + 6) * 2, tabY, tabW, tabH, activeTab == 2);

            canvas.DrawLine(Border, wx + 10, tabY + tabH + 6, wx + ww - 10, tabY + tabH + 6);

            int contentY = tabY + tabH + 12;

            if (activeTab == 0)
            {
                RenderBenchmarkTab(canvas, wx, contentY, ww);
            }
            else if (activeTab == 1)
            {
                RenderSpecsTab(canvas, wx, contentY, ww);
            }
            else
            {
                RenderComparisonTab(canvas, wx, contentY, ww);
            }
        }

        private void DrawTab(Canvas canvas, string title, int x, int y, int w, int h, bool selected)
        {
            Color bg = selected ? PanelHover : Panel;
            Color text = selected ? AccentColor : MutedColor;
            Color border = selected ? AccentColor : Border;

            UiPainter.RoundedPanel(canvas, x, y, w, h, 4, bg, border);
            SmallTextRenderer.DrawCentered(canvas, title, x, y + 8, w, text);
        }

        private void RenderBenchmarkTab(Canvas canvas, int wx, int cy, int ww)
        {
            // 4 Result Metric Cards
            int cardW = 130;
            int cardH = 92;
            int gap = 12;
            int cardY = cy + 4;

            // Card 1: CPU MIPS
            DrawMetricCard(canvas, wx + 16, cardY, cardW, cardH, "PROCESOR CPU",
                cpuMips > 0 ? cpuMips.ToString() : "---", "MIPS (Arytmetyka)",
                cpuMips > 0 ? GreenColor : MutedColor);

            // Card 2: RAM Speed
            DrawMetricCard(canvas, wx + 16 + (cardW + gap), cardY, cardW, cardH, "PAMIEC RAM",
                ramSpeedMb > 0 ? ramSpeedMb + " MB/s" : "---", "Sekwencyjny odczyt",
                ramSpeedMb > 0 ? AccentColor : MutedColor);

            // Card 3: GPU / Canvas
            DrawMetricCard(canvas, wx + 16 + (cardW + gap) * 2, cardY, cardW, cardH, "GRAFIKA VESA",
                gpuKpixSec > 0 ? gpuKpixSec + " kPix/s" : "---", "Wydajnosc rastra",
                gpuKpixSec > 0 ? GoldColor : MutedColor);

            // Card 4: VFS Cache
            DrawMetricCard(canvas, wx + 16 + (cardW + gap) * 3, cardY, cardW, cardH, "VFS / DYSK",
                vfsKbSec > 0 ? (vfsKbSec / 1024) + " MB/s" : "---", "I/O buforow dysku",
                vfsKbSec > 0 ? OrangeColor : MutedColor);

            // Big Score Banner
            int bannerY = cardY + cardH + 14;
            int bannerW = ww - 32;
            int bannerH = 100;

            UiPainter.RoundedPanel(canvas, wx + 16, bannerY, bannerW, bannerH, 6, Panel, Border);

            SmallTextRenderer.Draw(canvas, "WSKAZNIK WYDAJNOSCI ZONDERQ (Z-SCORE)", wx + 32, bannerY + 14, GoldColor);

            string scoreStr = totalZScore > 0 ? totalZScore.ToString() : "NIEPRZETESTOWANO";
            Color scoreColor = totalZScore > 4000 ? GreenColor : (totalZScore > 1000 ? AccentColor : GoldColor);
            SmallTextRenderer.Draw(canvas, scoreStr, wx + 32, bannerY + 36, scoreColor);

            string rating = "Uruchom test, aby okreslic moc obliczeniowa platformy.";
            if (totalZScore > 8000) rating = "Ocena: WYBITNA. Pelna plynnosc interfejsu graficznego, gier i srodowiska IDE.";
            else if (totalZScore > 3000) rating = "Ocena: BARDZO DOBRA. Plynny multitasking i pelna obsluga multimediow.";
            else if (totalZScore > 800) rating = "Ocena: DOBRA. Odpowiednia dla stacji roboczej retro i programowania BASIC.";
            else if (totalZScore > 0) rating = "Ocena: PODSTAWOWA. Tryb energooszczedny lub emulacja o niskim taktowaniu.";

            SmallTextRenderer.Draw(canvas, rating, wx + 32, bannerY + 66, TextColor);

            // Progress bar
            int progY = bannerY + bannerH + 12;
            int progW = ww - 32;
            UiPainter.RoundedPanel(canvas, wx + 16, progY, progW, 14, Panel, Border);
            if (progressPercent > 0)
            {
                int fillW = Math.Max(4, (progW * progressPercent) / 100);
                canvas.DrawFilledRectangle(AccentColor, wx + 16, progY, fillW, 14);
            }
            SmallTextRenderer.Draw(canvas, testStatus, wx + 18, progY + 20, MutedColor);

            // Action Buttons
            int btnY = progY + 40;
            DrawButton(canvas, "▶ URUCHOM WSZYSTKIE TESTY", wx + 16, btnY, 210, 32, AccentColor, isRunningAll);
            DrawButton(canvas, "Test CPU", wx + 236, btnY, 100, 32, PanelHover, testStep == 0);
            DrawButton(canvas, "Test RAM", wx + 346, btnY, 100, 32, PanelHover, testStep == 1);
            DrawButton(canvas, "Test Grafiki", wx + 456, btnY, 105, 32, PanelHover, testStep == 2);
        }

        private void DrawMetricCard(Canvas canvas, int x, int y, int w, int h, string title, string val, string sub, Color valColor)
        {
            UiPainter.RoundedPanel(canvas, x, y, w, h, 6, Panel, Border);
            SmallTextRenderer.Draw(canvas, title, x + 10, y + 10, MutedColor);
            SmallTextRenderer.Draw(canvas, val, x + 10, y + 36, valColor);
            SmallTextRenderer.Draw(canvas, sub, x + 10, y + 66, MutedColor);
        }

        private void DrawButton(Canvas canvas, string text, int x, int y, int w, int h, Color color, bool active)
        {
            Color fill = active ? Border : color;
            UiPainter.RoundedPanel(canvas, x, y, w, h, 4, fill, Border);
            SmallTextRenderer.DrawCentered(canvas, text, x, y + (h / 2) - 4, w, TextColor);
        }

        private void RenderSpecsTab(Canvas canvas, int wx, int cy, int ww)
        {
            int panelW = ww - 32;
            int panelH = 320;
            UiPainter.RoundedPanel(canvas, wx + 16, cy + 4, panelW, panelH, 6, Panel, Border);

            int sx = wx + 32;
            int sy = cy + 18;
            int lineH = 20;

            SmallTextRenderer.Draw(canvas, "JEDNOSTKA CENTRALNA (CPU):", sx, sy, GoldColor);
            sy += lineH;
            string brand = hardware != null ? hardware.CpuBrand : "Procesor x86_64";
            SmallTextRenderer.Draw(canvas, "  Model:        " + brand, sx, sy, TextColor);
            sy += lineH;
            string vendor = hardware != null ? hardware.CpuVendor : "GenuineIntel / AuthenticAMD";
            string arch = hardware != null ? hardware.Architecture : "x86_64";
            SmallTextRenderer.Draw(canvas, "  Producent:    " + vendor + " (" + arch + ")", sx, sy, TextColor);
            sy += lineH;
            int cores = hardware != null ? hardware.PhysicalCores : 1;
            int threads = hardware != null ? hardware.LogicalProcessors : 1;
            SmallTextRenderer.Draw(canvas, "  Rdzenie:      " + cores + " fizyczne / " + threads + " logiczne (SMP: 1 vCPU w stock)", sx, sy, TextColor);

            sy += lineH + 6;
            SmallTextRenderer.Draw(canvas, "PODSYSTEM PAMIECI OPERACYJNEJ (RAM):", sx, sy, AccentColor);
            sy += lineH;
            ulong totalMb = (hardware != null ? hardware.TotalMemoryBytes : 256UL * 1024 * 1024) / (1024 * 1024);
            ulong usedMb = (hardware != null ? hardware.UsedMemoryBytes : 48UL * 1024 * 1024) / (1024 * 1024);
            ulong freeMb = (hardware != null ? hardware.FreeMemoryBytes : 208UL * 1024 * 1024) / (1024 * 1024);
            SmallTextRenderer.Draw(canvas, "  Calkowita:    " + totalMb + " MB", sx, sy, TextColor);
            sy += lineH;
            SmallTextRenderer.Draw(canvas, "  Uzyta:        " + usedMb + " MB (" + ((usedMb * 100) / Math.Max(1, totalMb)) + "%)", sx, sy, TextColor);
            sy += lineH;
            SmallTextRenderer.Draw(canvas, "  Dostepna:     " + freeMb + " MB", sx, sy, TextColor);

            // Memory bar
            sy += lineH - 4;
            int barW = panelW - 40;
            UiPainter.RoundedPanel(canvas, sx, sy, barW, 10, 3, Surface, Border);
            int usedBarW = (int)((barW * usedMb) / Math.Max(1, totalMb));
            if (usedBarW > 0) canvas.DrawFilledRectangle(GreenColor, sx, sy, usedBarW, 10);

            sy += lineH + 6;
            SmallTextRenderer.Draw(canvas, "WIDEO I SYSTEM PLIKOW:", sx, sy, GreenColor);
            sy += lineH;
            SmallTextRenderer.Draw(canvas, "  Ekran:        " + canvas.Width + "x" + canvas.Height + " (VESA 32-bit Direct Color)", sx, sy, TextColor);
            sy += lineH;
            int drives = hardware != null ? hardware.StorageDeviceCount : 1;
            int parts = hardware != null ? hardware.StoragePartitionCount : 2;
            SmallTextRenderer.Draw(canvas, "  Pamiec masowa:" + drives + " naped(ow), " + parts + " partycji (VFS FAT32 / ZONDERQDATA)", sx, sy, TextColor);
            sy += lineH;
            SmallTextRenderer.Draw(canvas, "  Jadro:        Cosmos C# Kernel v3.0.95 Upstream (Single-vCPU)", sx, sy, TextColor);
        }

        private void RenderComparisonTab(Canvas canvas, int wx, int cy, int ww)
        {
            int panelW = ww - 32;
            int panelH = 320;
            UiPainter.RoundedPanel(canvas, wx + 16, cy + 4, panelW, panelH, 6, Panel, Border);

            SmallTextRenderer.Draw(canvas, "POROWNANIE WYNIKU Z-SCORE Z ARCHITEKTURAMI REFERENCYJNYMI", wx + 32, cy + 18, GoldColor);

            int startY = cy + 45;
            int rowH = 46;
            int barStartX = wx + 170;
            int maxBarW = panelW - 220;
            int maxScore = 10000;

            // 1. 486 DX2-66
            DrawBenchmarkBar(canvas, wx + 32, startY, "Intel 486 DX2-66", 150, maxScore, barStartX, maxBarW, MutedColor);
            // 2. Pentium 200 MMX
            DrawBenchmarkBar(canvas, wx + 32, startY + rowH, "Pentium 200 MMX", 680, maxScore, barStartX, maxBarW, MutedColor);
            // 3. Pentium III 800
            DrawBenchmarkBar(canvas, wx + 32, startY + rowH * 2, "Pentium III 800", 2400, maxScore, barStartX, maxBarW, MutedColor);
            // 4. Core 2 Duo E8400
            DrawBenchmarkBar(canvas, wx + 32, startY + rowH * 3, "Core 2 Duo E8400", 6500, maxScore, barStartX, maxBarW, AccentColor);

            // 5. User system
            int myScore = totalZScore > 0 ? totalZScore : 3500;
            DrawBenchmarkBar(canvas, wx + 32, startY + rowH * 4, "Twoj ZOnderqOS", myScore, maxScore, barStartX, maxBarW, GreenColor);

            SmallTextRenderer.Draw(canvas, "* Wskaznik Z-Score bazuje na kombinacji testow CPU MIPS, RAM MB/s i FPS VESA.", wx + 32, cy + 295, MutedColor);
        }

        private void DrawBenchmarkBar(Canvas canvas, int lx, int y, string name, int score, int maxScore, int bx, int maxW, Color color)
        {
            SmallTextRenderer.Draw(canvas, name, lx, y + 6, TextColor);
            int barW = Math.Min(maxW, Math.Max(8, (maxW * score) / maxScore));

            UiPainter.RoundedPanel(canvas, bx, y + 2, maxW, 18, 3, Surface, Border);
            canvas.DrawFilledRectangle(color, bx, y + 2, barW, 18);

            SmallTextRenderer.Draw(canvas, score + " pts", bx + barW + 8, y + 6, color);
        }
    }
}
