using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class TimerApp : Application
    {
        private static readonly Color Surface = Color.FromArgb(20, 25, 31);
        private static readonly Color Panel = Color.FromArgb(27, 34, 42);
        private static readonly Color PanelHover = Color.FromArgb(36, 45, 56);
        private static readonly Color PanelSelected = Color.FromArgb(44, 55, 68);
        private static readonly Color Border = Color.FromArgb(57, 70, 82);
        private static readonly Color TextColor = Color.FromArgb(234, 239, 244);
        private static readonly Color MutedColor = Color.FromArgb(132, 149, 164);
        private static readonly Color AccentColor = Color.FromArgb(73, 163, 224);
        private static readonly Color RedColor = Color.FromArgb(235, 75, 75);
        private static readonly Color GreenColor = Color.FromArgb(60, 200, 100);
        private static readonly Color YellowColor = Color.FromArgb(240, 195, 45);

        private static readonly string[] TabLabels = { "ZEGAR", "STOPER", "MINUTNIK" };
        private int activeTab = 0;

        // Stopwatch state
        private bool isStopwatchRunning;
        private long stopwatchStartTicks;
        private long stopwatchAccumulatedTicks;
        private readonly List<string> laps = new List<string>(16);

        // Countdown Timer state
        private bool isTimerRunning;
        private long timerTotalSeconds = 300; // default 5 minutes
        private long timerRemainingSeconds = 300;
        private long timerLastTick;
        private bool isAlarming;
        private int alarmBeepStep;
        private long nextAlarmTicks;

        private long toneEndTicks;
        private readonly Action closeCallback;

        public TimerApp(int x, int y, Action onClose) : base("Zegar i Czas")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 420, 420, "Zegar, Stoper i Minutnik");
            Window.CloseAction = Close;
        }

        public override bool NeedsContinuousRedraw => activeTab == 0 || isStopwatchRunning || isTimerRunning || isAlarming;

        public override void Close()
        {
            ZonderqOS.Hardware.Speaker.Stop();
            base.Close();
            closeCallback?.Invoke();
        }

        public override void Update()
        {
            long now = Stopwatch.GetTimestamp();
            long freq = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;

            // Tone shutoff
            if (toneEndTicks > 0 && now >= toneEndTicks)
            {
                ZonderqOS.Hardware.Speaker.Stop();
                toneEndTicks = 0;
            }

            // Countdown timer tick
            if (isTimerRunning)
            {
                if (timerLastTick == 0) timerLastTick = now;
                if (now - timerLastTick >= freq)
                {
                    long elapsedSecs = (now - timerLastTick) / freq;
                    timerLastTick += elapsedSecs * freq;
                    timerRemainingSeconds -= elapsedSecs;

                    if (timerRemainingSeconds <= 0)
                    {
                        timerRemainingSeconds = 0;
                        isTimerRunning = false;
                        isAlarming = true;
                        alarmBeepStep = 0;
                        nextAlarmTicks = now;
                    }
                }
            }

            // Timer Alarm Beeping (3 double beeps)
            if (isAlarming)
            {
                if (now >= nextAlarmTicks)
                {
                    if (alarmBeepStep < 6)
                    {
                        if (alarmBeepStep % 2 == 0)
                        {
                            PlayTone(950, 100);
                            nextAlarmTicks = now + (freq * 120) / 1000;
                        }
                        else
                        {
                            nextAlarmTicks = now + (freq * 400) / 1000;
                        }
                        alarmBeepStep++;
                    }
                    else
                    {
                        isAlarming = false;
                    }
                }
            }
        }

        private void PlayTone(ushort freq, int durationMs)
        {
            ZonderqOS.Hardware.Speaker.Tone(freq);
            long clock = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;
            toneEndTicks = Stopwatch.GetTimestamp() + (clock * durationMs) / 1000;
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key == null)
                return;

            if (key.Key == ConsoleKeyEx.Escape)
            {
                if (isAlarming)
                {
                    isAlarming = false;
                    return;
                }
                Close();
                return;
            }

            if (key.Key == ConsoleKeyEx.Tab)
            {
                activeTab = (activeTab + 1) % TabLabels.Length;
                PlayTone(550, 15);
                return;
            }

            if (key.Key == ConsoleKeyEx.Spacebar)
            {
                if (activeTab == 1)
                {
                    ToggleStopwatch();
                }
                else if (activeTab == 2)
                {
                    ToggleTimer();
                }
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);

            if (!Window.Visible || Window.IsMinimized)
                return;

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;
            int contentWidth = Math.Max(1, Window.Width - 16);

            // Tab bar hit test (Top)
            int tabY = contentY + 6;
            int tabW = (contentWidth - 28 - 2 * 6) / 3;
            int tabH = 30;

            if (leftClicked && !leftWasClicked)
            {
                for (int i = 0; i < 3; i++)
                {
                    int tx = contentX + 14 + i * (tabW + 6);
                    if (mouseX >= tx && mouseX < tx + tabW && mouseY >= tabY && mouseY < tabY + tabH)
                    {
                        activeTab = i;
                        if (isAlarming) isAlarming = false;
                        PlayTone(550, 15);
                        return;
                    }
                }
            }

            // Tab 1: Stopwatch interactions
            if (activeTab == 1 && leftClicked && !leftWasClicked)
            {
                int btnY = contentY + 140;
                int btnW = 90;
                int btnH = 28;

                // [START / PAUZA]
                if (Hit(mouseX, mouseY, contentX + 35, btnY, btnW, btnH))
                {
                    ToggleStopwatch();
                    return;
                }
                // [LAP]
                if (Hit(mouseX, mouseY, contentX + 155, btnY, btnW, btnH))
                {
                    AddLap();
                    return;
                }
                // [RESET]
                if (Hit(mouseX, mouseY, contentX + 275, btnY, btnW, btnH))
                {
                    ResetStopwatch();
                    return;
                }
            }

            // Tab 2: Countdown Timer interactions
            if (activeTab == 2 && leftClicked && !leftWasClicked)
            {
                if (isAlarming)
                {
                    isAlarming = false;
                    return;
                }

                // Time adjustment buttons: [+1 min], [+5 min], [+10 min], [-1 min]
                int adjY = contentY + 160;
                int adjW = 68;
                int adjH = 24;

                if (Hit(mouseX, mouseY, contentX + 30, adjY, adjW, adjH)) { AdjustTimer(60); return; }
                if (Hit(mouseX, mouseY, contentX + 115, adjY, adjW, adjH)) { AdjustTimer(300); return; }
                if (Hit(mouseX, mouseY, contentX + 200, adjY, adjW, adjH)) { AdjustTimer(600); return; }
                if (Hit(mouseX, mouseY, contentX + 285, adjY, adjW, adjH)) { AdjustTimer(-60); return; }

                // Timer Action buttons: [START / PAUZA], [RESET]
                int actY = contentY + 210;
                if (Hit(mouseX, mouseY, contentX + 85, actY, 105, 30)) { ToggleTimer(); return; }
                if (Hit(mouseX, mouseY, contentX + 210, actY, 105, 30)) { ResetTimer(); return; }
            }
        }

        private static bool Hit(int mx, int my, int x, int y, int w, int h)
        {
            return mx >= x && mx < x + w && my >= y && my < y + h;
        }

        private void ToggleStopwatch()
        {
            long now = Stopwatch.GetTimestamp();
            if (isStopwatchRunning)
            {
                stopwatchAccumulatedTicks += (now - stopwatchStartTicks);
                isStopwatchRunning = false;
                PlayTone(450, 20);
            }
            else
            {
                stopwatchStartTicks = now;
                isStopwatchRunning = true;
                PlayTone(650, 20);
            }
        }

        private void AddLap()
        {
            if (laps.Count >= 8)
                laps.RemoveAt(0);

            long totalTicks = GetCurrentStopwatchTicks();
            long totalMs = (totalTicks * 1000) / (Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000);
            long mins = totalMs / 60000;
            long secs = (totalMs % 60000) / 1000;
            long cs = (totalMs % 1000) / 10;

            string lapStr = "Lap " + (laps.Count + 1) + ":  " +
                mins.ToString("00") + ":" + secs.ToString("00") + "." + cs.ToString("00");

            laps.Add(lapStr);
            PlayTone(750, 20);
        }

        private void ResetStopwatch()
        {
            isStopwatchRunning = false;
            stopwatchStartTicks = 0;
            stopwatchAccumulatedTicks = 0;
            laps.Clear();
            PlayTone(400, 20);
        }

        private long GetCurrentStopwatchTicks()
        {
            long ticks = stopwatchAccumulatedTicks;
            if (isStopwatchRunning)
            {
                ticks += (Stopwatch.GetTimestamp() - stopwatchStartTicks);
            }
            return ticks;
        }

        private void ToggleTimer()
        {
            if (isTimerRunning)
            {
                isTimerRunning = false;
                PlayTone(450, 20);
            }
            else
            {
                if (timerRemainingSeconds <= 0)
                    timerRemainingSeconds = timerTotalSeconds;

                timerLastTick = Stopwatch.GetTimestamp();
                isTimerRunning = true;
                isAlarming = false;
                PlayTone(650, 20);
            }
        }

        private void AdjustTimer(int deltaSeconds)
        {
            if (isTimerRunning)
                return;

            timerTotalSeconds = Math.Max(10, timerTotalSeconds + deltaSeconds);
            timerRemainingSeconds = timerTotalSeconds;
            PlayTone(520, 15);
        }

        private void ResetTimer()
        {
            isTimerRunning = false;
            isAlarming = false;
            timerRemainingSeconds = timerTotalSeconds;
            PlayTone(400, 20);
        }

        public override void Render(Canvas canvas)
        {
            if (!Window.Visible || Window.IsMinimized)
                return;

            base.Render(canvas);

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;
            int contentWidth = Math.Max(1, Window.Width - 16);
            int contentHeight = Math.Max(1, Window.Height - 46);

            // Background
            canvas.DrawFilledRectangle(Surface, contentX, contentY, contentWidth, contentHeight);

            // Tabs Bar (Top)
            int tabY = contentY + 6;
            int tabW = (contentWidth - 28 - 2 * 6) / 3;
            int tabH = 30;

            for (int i = 0; i < 3; i++)
            {
                int tx = contentX + 14 + i * (tabW + 6);
                bool isSelected = (i == activeTab);

                Color bg = isSelected ? PanelSelected : Panel;
                Color border = isSelected ? SystemTheme.AccentBorder : Border;

                canvas.DrawFilledRectangle(bg, tx, tabY, tabW, tabH);
                canvas.DrawRectangle(border, tx, tabY, tabW, tabH);

                if (isSelected)
                    canvas.DrawFilledRectangle(SystemTheme.Accent, tx, tabY + tabH - 3, tabW, 3);

                SmallTextRenderer.DrawCentered(canvas, TabLabels[i], tx, tabY + 11, tabW, isSelected ? SystemTheme.Accent : TextColor);
            }

            // Render Tab Content
            if (activeTab == 0)
            {
                RenderAnalogClock(canvas, contentX, contentY + 44, contentWidth);
            }
            else if (activeTab == 1)
            {
                RenderStopwatch(canvas, contentX, contentY + 44, contentWidth);
            }
            else
            {
                RenderTimer(canvas, contentX, contentY + 44, contentWidth);
            }
        }

        private void RenderAnalogClock(Canvas canvas, int cx, int cy, int cw)
        {
            DateTime now = DateTime.UtcNow.AddHours(global::ZonderqOS.SystemSettings.TimeZoneOffsetHours);

            int centerX = cx + cw / 2;
            int centerY = cy + 96;
            int radius = 75;

            // Clock face circle outline / background
            canvas.DrawFilledRectangle(Color.FromArgb(14, 18, 24), centerX - radius, centerY - radius, radius * 2, radius * 2);
            canvas.DrawRectangle(Border, centerX - radius, centerY - radius, radius * 2, radius * 2);
            canvas.DrawRectangle(SystemTheme.AccentBorder, centerX - radius + 2, centerY - radius + 2, radius * 2 - 4, radius * 2 - 4);

            // Hour tick marks (12, 3, 6, 9)
            SmallTextRenderer.DrawCentered(canvas, "12", centerX - 10, centerY - radius + 6, 20, TextColor);
            SmallTextRenderer.DrawCentered(canvas, "6", centerX - 10, centerY + radius - 14, 20, TextColor);
            SmallTextRenderer.Draw(canvas, "9", centerX - radius + 6, centerY - 4, TextColor);
            SmallTextRenderer.Draw(canvas, "3", centerX + radius - 12, centerY - 4, TextColor);

            // Clock hands
            float secRad = (now.Second * 6.0f) * (3.14159265f / 180.0f);
            float minRad = (now.Minute * 6.0f + now.Second * 0.1f) * (3.14159265f / 180.0f);
            float hourRad = ((now.Hour % 12) * 30.0f + now.Minute * 0.5f) * (3.14159265f / 180.0f);

            // Hour hand (short thick)
            int hx = centerX + (int)(FastSin(hourRad) * 42);
            int hy = centerY - (int)(FastCos(hourRad) * 42);
            canvas.DrawLine(TextColor, centerX, centerY, hx, hy);
            canvas.DrawLine(TextColor, centerX + 1, centerY, hx + 1, hy);

            // Minute hand (longer)
            int mx = centerX + (int)(FastSin(minRad) * 60);
            int my = centerY - (int)(FastCos(minRad) * 60);
            canvas.DrawLine(SystemTheme.Accent, centerX, centerY, mx, my);

            // Second hand (thin red)
            int sx = centerX + (int)(FastSin(secRad) * 66);
            int sy = centerY - (int)(FastCos(secRad) * 66);
            canvas.DrawLine(RedColor, centerX, centerY, sx, sy);

            // Center pin
            canvas.DrawFilledRectangle(Color.White, centerX - 2, centerY - 2, 5, 5);

            // Digital clock display below
            int digY = cy + 195;
            string timeStr = now.Hour.ToString("00") + ":" + now.Minute.ToString("00") + ":" + now.Second.ToString("00");
            canvas.DrawFilledRectangle(Panel, centerX - 80, digY, 160, 32);
            canvas.DrawRectangle(Border, centerX - 80, digY, 160, 32);
            SmallTextRenderer.DrawCentered(canvas, timeStr, centerX - 80, digY + 12, 160, TextColor);

            // Date and Timezone
            string dateStr = now.Day.ToString("00") + "." + now.Month.ToString("00") + "." + now.Year;
            string tzStr = "Strefa: UTC" + (global::ZonderqOS.SystemSettings.TimeZoneOffsetHours >= 0 ? "+" : "") +
                global::ZonderqOS.SystemSettings.TimeZoneOffsetHours;

            SmallTextRenderer.DrawCentered(canvas, dateStr, cx, digY + 42, cw, MutedColor);
            SmallTextRenderer.DrawCentered(canvas, tzStr, cx, digY + 56, cw, SystemTheme.Accent);
        }

        private void RenderStopwatch(Canvas canvas, int cx, int cy, int cw)
        {
            long totalTicks = GetCurrentStopwatchTicks();
            long totalMs = (totalTicks * 1000) / (Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000);
            long mins = totalMs / 60000;
            long secs = (totalMs % 60000) / 1000;
            long cs = (totalMs % 1000) / 10;

            string displayStr = mins.ToString("00") + ":" + secs.ToString("00") + "." + cs.ToString("00");

            // Big Stopwatch Display
            int dispY = cy + 20;
            int dispW = 260;
            int dispX = cx + (cw - dispW) / 2;

            canvas.DrawFilledRectangle(Color.FromArgb(14, 18, 24), dispX, dispY, dispW, 58);
            canvas.DrawRectangle(isStopwatchRunning ? SystemTheme.AccentBorder : Border, dispX, dispY, dispW, 58);
            SmallTextRenderer.DrawCentered(canvas, displayStr, dispX, dispY + 25, dispW, isStopwatchRunning ? SystemTheme.Accent : TextColor);

            // Stopwatch Buttons: [START/PAUZA], [LAP], [RESET]
            int btnY = cy + 96;
            int btnW = 90;
            int btnH = 28;

            DrawBtn(canvas, isStopwatchRunning ? "PAUZA" : "START", cx + 35, btnY, btnW, btnH, isStopwatchRunning ? RedColor : GreenColor);
            DrawBtn(canvas, "OKRĄŻENIE", cx + 155, btnY, btnW, btnH, AccentColor);
            DrawBtn(canvas, "RESET", cx + 275, btnY, btnW, btnH, TextColor);

            // Laps List Box
            int lapBoxY = cy + 140;
            int lapBoxW = cw - 40;
            int lapBoxX = cx + 20;
            int lapBoxH = 150;

            canvas.DrawFilledRectangle(Panel, lapBoxX, lapBoxY, lapBoxW, lapBoxH);
            canvas.DrawRectangle(Border, lapBoxX, lapBoxY, lapBoxW, lapBoxH);

            SmallTextRenderer.Draw(canvas, "ZAREJESTROWANE OKRĄŻENIA (LAPS):", lapBoxX + 12, lapBoxY + 10, MutedColor);

            if (laps.Count == 0)
            {
                SmallTextRenderer.DrawCentered(canvas, "Brak okrążen. Kliknij [OKRĄŻENIE] w trakcie pomiaru.", lapBoxX, lapBoxY + 65, lapBoxW, MutedColor);
            }
            else
            {
                for (int i = 0; i < laps.Count; i++)
                {
                    int ly = lapBoxY + 30 + i * 14;
                    SmallTextRenderer.Draw(canvas, laps[i], lapBoxX + 16, ly, TextColor);
                }
            }
        }

        private void RenderTimer(Canvas canvas, int cx, int cy, int cw)
        {
            long mins = timerRemainingSeconds / 60;
            long secs = timerRemainingSeconds % 60;
            string timerStr = mins.ToString("00") + ":" + secs.ToString("00");

            // Big Timer Display
            int dispY = cy + 20;
            int dispW = 260;
            int dispX = cx + (cw - dispW) / 2;

            Color numColor = isAlarming ? RedColor : (isTimerRunning ? GreenColor : TextColor);
            canvas.DrawFilledRectangle(Color.FromArgb(14, 18, 24), dispX, dispY, dispW, 58);
            canvas.DrawRectangle(isAlarming ? RedColor : (isTimerRunning ? GreenColor : Border), dispX, dispY, dispW, 58);
            SmallTextRenderer.DrawCentered(canvas, timerStr, dispX, dispY + 25, dispW, numColor);

            // Progress bar
            int barY = cy + 88;
            int barW = 260;
            int barX = dispX;
            canvas.DrawFilledRectangle(Color.FromArgb(20, 26, 34), barX, barY, barW, 8);
            canvas.DrawRectangle(Border, barX, barY, barW, 8);

            float pct = timerTotalSeconds > 0 ? (float)timerRemainingSeconds / timerTotalSeconds : 0;
            int fillW = (int)(barW * Math.Max(0.0f, Math.Min(1.0f, pct)));
            if (fillW > 0)
                canvas.DrawFilledRectangle(SystemTheme.Accent, barX, barY, fillW, 8);

            // Adjustment buttons: [+1 min], [+5 min], [+10 min], [-1 min]
            int adjY = cy + 116;
            int adjW = 68;
            int adjH = 24;

            DrawBtn(canvas, "+1 MIN", cx + 30, adjY, adjW, adjH, TextColor);
            DrawBtn(canvas, "+5 MIN", cx + 115, adjY, adjW, adjH, TextColor);
            DrawBtn(canvas, "+10 MIN", cx + 200, adjY, adjW, adjH, TextColor);
            DrawBtn(canvas, "-1 MIN", cx + 285, adjY, adjW, adjH, TextColor);

            // Action Buttons: [START / PAUZA], [RESET]
            int actY = cy + 166;
            DrawBtn(canvas, isTimerRunning ? "PAUZA" : "START", cx + 85, actY, 105, 30, isTimerRunning ? RedColor : GreenColor);
            DrawBtn(canvas, "RESET", cx + 210, actY, 105, 30, TextColor);

            // Alarm Message Banner
            if (isAlarming)
            {
                int almY = cy + 225;
                canvas.DrawFilledRectangle(Color.FromArgb(60, 20, 20), dispX, almY, dispW, 36);
                canvas.DrawRectangle(RedColor, dispX, almY, dispW, 36);
                SmallTextRenderer.DrawCentered(canvas, "ALARM! CZAS MINĄŁ!", dispX, almY + 14, dispW, RedColor);
            }
        }

        private static void DrawBtn(Canvas canvas, string text, int x, int y, int w, int h, Color color)
        {
            canvas.DrawFilledRectangle(Panel, x, y, w, h);
            canvas.DrawRectangle(Border, x, y, w, h);
            SmallTextRenderer.DrawCentered(canvas, text, x, y + (h - 7) / 2, w, color);
        }

        private static float FastSin(float rad)
        {
            const float PI = 3.14159265f;
            const float TWO_PI = 6.2831853f;
            rad = rad % TWO_PI;
            if (rad > PI) rad -= TWO_PI;
            if (rad < -PI) rad += TWO_PI;

            float abs = rad < 0 ? -rad : rad;
            float num = 16.0f * rad * (PI - abs);
            float den = 5.0f * PI * PI - 4.0f * abs * (PI - abs);
            return den != 0 ? num / den : 0;
        }

        private static float FastCos(float rad) => FastSin(rad + 1.57079632f);
    }
}
