using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Shared fixed-layout shell for advanced settings tools. Rendering uses fixed
    /// strings and cached snapshots; filesystem/hardware queries happen only on open,
    /// F5 or an explicit action.
    /// </summary>
    public abstract class SettingsToolWindow : Application
    {
        protected static readonly Color Surface = Color.FromArgb(24, 29, 35);
        protected static readonly Color Panel = Color.FromArgb(31, 38, 46);
        protected static readonly Color PanelHover = Color.FromArgb(38, 50, 61);
        protected static readonly Color Border = Color.FromArgb(53, 66, 78);
        protected static readonly Color Text = Color.FromArgb(232, 237, 242);
        protected static readonly Color Muted = Color.FromArgb(132, 149, 164);
        protected static readonly Color Good = Color.FromArgb(78, 185, 126);
        protected static readonly Color Warning = Color.FromArgb(224, 174, 76);
        protected static readonly Color Danger = Color.FromArgb(215, 86, 91);

        protected int LastMouseX = -1;
        protected int LastMouseY = -1;
        protected string StatusMessage = "GOTOWE";
        protected Color StatusColor = Good;
        private readonly string headerName;

        protected SettingsToolWindow(string appName, string title, int x, int y, int width = 900, int height = 620)
            : base(appName)
        {
            headerName = string.IsNullOrEmpty(appName) ? "USTAWIENIA" : appName.ToUpperInvariant();
            Window = new Window(x, y, width, height, title);
            Window.CloseAction = Close;
        }

        protected Color Accent
        {
            get { return global::ZonderqOS.GUI.SystemTheme.Accent; }
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key == null)
                return;
            if (key.Key == ConsoleKeyEx.Escape)
            {
                Close();
                return;
            }
            if (key.Key == ConsoleKeyEx.F5)
            {
                RefreshData();
                SetStatus("ODSWIEZONO", Good);
                return;
            }
            OnKeyboard(key);
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            LastMouseX = mouseX;
            LastMouseY = mouseY;
            Window?.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked);
            if (!IsRunning || Window == null || !Window.Visible || Window.IsMinimized)
                return;
            if (leftClicked && !leftWasClicked)
                OnClick(mouseX, mouseY);
        }

        public override void Render(Canvas canvas)
        {
            if (!IsRunning || Window == null || !Window.Visible || Window.IsMinimized)
                return;

            Window.Render(canvas);
            int top = Window.Y + 39;
            canvas.DrawFilledRectangle(Surface, Window.X + 1, top, Window.Width - 2, Window.Height - 40);
            RenderHeader(canvas);
            RenderContent(canvas);
            RenderStatus(canvas);
        }

        protected virtual void RenderHeader(Canvas canvas)
        {
            SmallTextRenderer.Draw(canvas, headerName, Window.X + 22, Window.Y + 58, Text);
            SmallTextRenderer.Draw(canvas, "F5 ODSWIEZ  |  ESC ZAMKNIJ", Window.X + 22, Window.Y + 78, Muted);
            canvas.DrawLine(Border, Window.X + 22, Window.Y + 100, Window.X + Window.Width - 22, Window.Y + 100);
        }

        protected void DrawRow(Canvas canvas, int row, IconType icon, string title, string description,
            string value, Color valueColor)
        {
            int x = Window.X + 22;
            int y = RowY(row);
            int width = Window.Width - 44;
            bool hover = Hit(LastMouseX, LastMouseY, x, y, width, 58);
            canvas.DrawFilledRectangle(hover ? PanelHover : Panel, x, y, width, 58);
            canvas.DrawRectangle(hover ? Accent : Border, x, y, width, 58);
            if (hover)
                canvas.DrawFilledRectangle(Accent, x, y + 5, 3, 48);

            IconManager.DrawScaled(canvas, icon, x + 13, y + 17, 22, 22);
            int chipWidth = System.Math.Min(220, System.Math.Max(110, width / 4));
            int textWidth = System.Math.Max(80, width - chipWidth - 74);
            SmallTextRenderer.DrawClipped(canvas, title, x + 48, y + 13, textWidth, Text);
            SmallTextRenderer.DrawClipped(canvas, description, x + 48, y + 34, textWidth, Muted);

            int chipX = x + width - chipWidth - 12;
            canvas.DrawFilledRectangle(Color.FromArgb(24, 30, 36), chipX, y + 12, chipWidth, 34);
            canvas.DrawRectangle(Border, chipX, y + 12, chipWidth, 34);
            SmallTextRenderer.DrawCentered(canvas, value ?? string.Empty, chipX + 7, y + 26,
                System.Math.Max(20, chipWidth - 14), valueColor);
        }

        protected void DrawNumericRow(Canvas canvas, int row, IconType icon, string title, string description,
            ulong value, string suffix)
        {
            DrawRow(canvas, row, icon, title, description, string.Empty, Text);
            int x = Window.X + 22;
            int width = Window.Width - 44;
            int chipWidth = System.Math.Min(220, System.Math.Max(110, width / 4));
            int chipX = x + width - chipWidth - 12;
            int numberWidth = SmallTextRenderer.WidthUInt(value);
            int suffixWidth = string.IsNullOrEmpty(suffix) ? 0 : SmallTextRenderer.Width(suffix) + 6;
            int drawX = chipX + System.Math.Max(8, (chipWidth - numberWidth - suffixWidth) / 2);
            SmallTextRenderer.DrawUInt(canvas, value, drawX, RowY(row) + 26, Text);
            if (!string.IsNullOrEmpty(suffix))
                SmallTextRenderer.Draw(canvas, suffix, drawX + numberWidth + 6, RowY(row) + 26, Muted);
        }

        protected void RenderStatus(Canvas canvas)
        {
            int x = Window.X + 22;
            int y = Window.Y + Window.Height - 39;
            int width = Window.Width - 44;
            canvas.DrawFilledRectangle(Color.FromArgb(20, 25, 31), x, y, width, 26);
            canvas.DrawRectangle(Border, x, y, width, 26);
            canvas.DrawFilledRectangle(StatusColor, x + 9, y + 10, 5, 5);
            SmallTextRenderer.DrawClipped(canvas, StatusMessage, x + 24, y + 10,
                System.Math.Max(40, width - 34), StatusColor);
        }

        protected int RowY(int row)
        {
            return Window.Y + 114 + row * 64;
        }

        protected int HitRow(int mouseX, int mouseY, int rowCount)
        {
            int x = Window.X + 22;
            int width = Window.Width - 44;
            for (int i = 0; i < rowCount; i++)
            {
                if (Hit(mouseX, mouseY, x, RowY(i), width, 58))
                    return i;
            }
            return -1;
        }

        protected static bool Hit(int px, int py, int x, int y, int width, int height)
        {
            return px >= x && px < x + width && py >= y && py < y + height;
        }

        protected void SetStatus(string message, Color color)
        {
            StatusMessage = string.IsNullOrEmpty(message) ? "GOTOWE" : message;
            StatusColor = color;
        }

        protected abstract void RenderContent(Canvas canvas);
        protected abstract void RefreshData();
        protected virtual void OnClick(int mouseX, int mouseY) { }
        protected virtual void OnKeyboard(KeyEvent key) { }
    }
}
