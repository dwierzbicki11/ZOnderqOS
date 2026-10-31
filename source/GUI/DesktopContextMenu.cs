using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI
{
    public sealed class DesktopContextMenu : Widget
    {
        private sealed class MenuEntry
        {
            public readonly string Label;
            public readonly IconType Icon;
            public readonly Action Action;

            public MenuEntry(string label, IconType icon, Action action)
            {
                Label = label;
                Icon = icon;
                Action = action;
            }
        }

        private readonly MenuEntry[] entries;
        private int hoveredIndex = -1;
        private int lastMouseX, lastMouseY;

        private const int MenuWidth = 222;
        private const int Padding = 6;
        private const int ItemHeight = 36;

        public DesktopContextMenu(Action openTerminal, Action openFileManager, Action openNotepad,
            Action refreshDesktop, Action openSystemInfo)
            : base(0, 0, MenuWidth, Padding * 2 + ItemHeight * 5)
        {
            entries = new[]
            {
                new MenuEntry("Open Terminal", IconType.Terminal, openTerminal),
                new MenuEntry("Open File Manager", IconType.Folder, openFileManager),
                new MenuEntry("Open Notepad", IconType.File, openNotepad),
                new MenuEntry("Refresh Desktop", IconType.Refresh, refreshDesktop),
                new MenuEntry("System Info", IconType.Settings, openSystemInfo)
            };

            Visible = false;
        }

        public void ShowAt(int x, int y, int desktopWidth, int desktopHeight)
        {
            X = Math.Max(6, Math.Min(x, desktopWidth - Width - 6));
            Y = Math.Max(6, Math.Min(y, desktopHeight - Height - 6));
            hoveredIndex = -1;
            // Preserve keyboard selection while the pointer remains stationary.
            // The opening right-click must not immediately overwrite it.
            lastMouseX = x;
            lastMouseY = y;
            Visible = true;
            UiAnimator.RequestFrames(6);
        }

        // The desktop menu is modal: never forward typing or destructive keys
        // to the application underneath while it is open.
        public bool HandleKeyboard(KeyEvent key)
        {
            if (!Visible || key == null)
                return false;

            if (key.Key == ConsoleKeyEx.Escape)
            {
                Visible = false;
                hoveredIndex = -1;
                return true;
            }

            // Ctrl/Alt/Shift combinations cannot launch a menu command.
            if (key.Modifiers != 0)
                return true;

            if (key.Key == ConsoleKeyEx.UpArrow)
                hoveredIndex = hoveredIndex < 0 ? entries.Length - 1 :
                    (hoveredIndex + entries.Length - 1) % entries.Length;
            else if (key.Key == ConsoleKeyEx.DownArrow)
                hoveredIndex = (hoveredIndex + 1) % entries.Length;
            else if (key.Key == ConsoleKeyEx.Home)
                hoveredIndex = 0;
            else if (key.Key == ConsoleKeyEx.End)
                hoveredIndex = entries.Length - 1;
            else if (key.Key == ConsoleKeyEx.Enter || key.Key == ConsoleKeyEx.NumEnter)
            {
                int index = hoveredIndex;
                Visible = false;
                hoveredIndex = -1;
                if (index >= 0 && index < entries.Length)
                    entries[index].Action?.Invoke();
                return true;
            }
            else
                return true;

            UiAnimator.RequestFrames();
            return true;
        }

        public bool UpdateInteractions(int mouseX, int mouseY, bool left, bool oldLeft)
        {
            if (!Visible)
                return false;

            // A stationary mouse must not erase arrow-key navigation each frame.
            // A click always acts on the actual pointer target, not the keyboard highlight.
            int pointerIndex = HitTest(mouseX, mouseY);
            if (mouseX != lastMouseX || mouseY != lastMouseY)
            {
                hoveredIndex = pointerIndex;
                lastMouseX = mouseX;
                lastMouseY = mouseY;
                UiAnimator.RequestFrames();
            }
            if (!left || oldLeft)
                return false;

            int index = pointerIndex;
            Visible = false;
            hoveredIndex = -1;

            if (index >= 0 && index < entries.Length)
            {
                entries[index].Action?.Invoke();
                return true;
            }

            return true;
        }

        private int HitTest(int mouseX, int mouseY)
        {
            if (mouseX < X || mouseX >= X + Width || mouseY < Y || mouseY >= Y + Height)
                return -1;

            int localY = mouseY - Y - Padding;
            if (localY < 0)
                return -1;

            int index = localY / ItemHeight;
            return index >= 0 && index < entries.Length ? index : -1;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible)
                return;

            UiPainter.Shadow(canvas, X, Y, Width, Height, UiMetrics.MenuRadius);
            UiPainter.RoundedPanel(canvas, X, Y, Width, Height, UiMetrics.MenuRadius,
                UiPalette.Surface, UiPalette.Border);

            for (int i = 0; i < entries.Length; i++)
            {
                int itemY = Y + Padding + i * ItemHeight;
                if (i == hoveredIndex)
                {
                    UiPainter.RoundedFill(canvas, X + Padding, itemY,
                        Width - Padding * 2, ItemHeight - 1, UiMetrics.TileRadius,
                        UiPalette.SurfaceHover);
                    canvas.DrawFilledRectangle(SystemTheme.Accent, X + Padding + 6,
                        itemY + ItemHeight - 4, Width - Padding * 2 - 12, 2);
                }

                UiPainter.RoundedFill(canvas, X + 14, itemY + 6, 24, 24,
                    UiMetrics.TileRadius, UiPalette.SurfaceRaised);
                IconManager.DrawScaled(canvas, entries[i].Icon, X + 17, itemY + 9, 18, 18);
                SmallTextRenderer.DrawClipped(canvas, entries[i].Label, X + 48, itemY + 15,
                    Width - 60, i == hoveredIndex ? Color.WhiteSmoke : Color.FromArgb(221, 228, 234));
            }
        }
    }
}
