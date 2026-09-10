using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI
{
    public class StartMenu : Widget
    {
        public List<Button> Items { get; } = new List<Button>();

        private readonly List<string> itemLabels = new List<string>();
        private readonly List<IconType> itemIcons = new List<IconType>();
        private readonly ScrollBar scrollBar;
        private int scrollOffset;

        private const int ItemHeight = 44;
        private const int Padding = 10;
        private const int HeaderHeight = 72;
        private const int SectionHeight = 18;
        private const int FooterHeight = 38;

        public StartMenu(int x, int y, int width, int height) : base(x, y, width, height)
        {
            Visible = false;
            scrollBar = new ScrollBar(X + Width - 15, GetContentY(), 8, GetContentHeight());
            scrollBar.ValueChanged = value => scrollOffset = value;
        }

        public void AddItem(string text, Action onClick)
        {
            AddItem(text, IconType.File, onClick);
        }

        public void AddItem(string text, IconType icon, Action onClick)
        {
            var button = new Button(X + Padding, GetContentY(), Width - 30, ItemHeight - 6, string.Empty, () =>
            {
                onClick?.Invoke();
                Visible = false;
            });

            button.BackgroundColor = Color.FromArgb(38, 45, 53);
            button.TextColor = Color.FromArgb(232, 236, 240);
            Items.Add(button);
            itemLabels.Add(text ?? string.Empty);
            itemIcons.Add(icon);
            UpdateLayout();
        }

        private int GetContentY()
        {
            return Y + HeaderHeight + SectionHeight + Padding;
        }

        private int GetContentHeight()
        {
            return Math.Max(20, Height - HeaderHeight - SectionHeight - FooterHeight - Padding * 2);
        }

        private int GetVisibleItemCount()
        {
            return Math.Max(1, GetContentHeight() / ItemHeight);
        }

        private void UpdateLayout()
        {
            int visibleCount = GetVisibleItemCount();
            int maxOffset = Math.Max(0, Items.Count - visibleCount);
            scrollOffset = Math.Max(0, Math.Min(scrollOffset, maxOffset));

            int contentY = GetContentY();
            int contentHeight = GetContentHeight();

            scrollBar.X = X + Width - 15;
            scrollBar.Y = contentY;
            scrollBar.Width = 8;
            scrollBar.Height = contentHeight;
            scrollBar.SetRange(Items.Count, visibleCount);
            scrollBar.Value = scrollOffset;

            for (int i = 0; i < Items.Count; i++)
            {
                int visibleIndex = i - scrollOffset;
                Items[i].X = X + Padding;
                Items[i].Y = contentY + visibleIndex * ItemHeight;
                Items[i].Width = Math.Max(30, Width - 30);
                Items[i].Height = ItemHeight - 6;
                Items[i].Visible = visibleIndex >= 0 && visibleIndex < visibleCount;
            }
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible)
                return;

            UpdateLayout();

            Color shadow = Color.FromArgb(13, 17, 21);
            Color background = Color.FromArgb(25, 30, 36);
            Color header = Color.FromArgb(31, 39, 47);
            Color footer = Color.FromArgb(21, 26, 32);
            Color accent = Color.FromArgb(64, 143, 204);
            Color border = Color.FromArgb(67, 80, 92);

            canvas.DrawFilledRectangle(shadow, X + 6, Y + 6, Width, Height);
            canvas.DrawFilledRectangle(background, X, Y, Width, Height);
            canvas.DrawRectangle(border, X, Y, Width, Height);

            canvas.DrawFilledRectangle(header, X + 1, Y + 1, Width - 2, HeaderHeight - 1);
            canvas.DrawFilledRectangle(accent, X + 1, Y + 1, 4, HeaderHeight - 1);
            IconManager.DrawScaled(canvas, IconType.Start, X + 16, Y + 14, 28, 28);
            canvas.DrawString("ZOnderqOS", PCScreenFont.DefaultFont, Color.FromArgb(241, 244, 247), X + 54, Y + 7);
            DrawTinyText(canvas, "MODERN DESKTOP  GEN3", X + 56, Y + 45, Color.FromArgb(133, 157, 178));
            canvas.DrawLine(Color.FromArgb(55, 69, 82), X + 1, Y + HeaderHeight, X + Width - 2, Y + HeaderHeight);

            DrawTinyText(canvas, "APLIKACJE", X + 14, Y + HeaderHeight + 7, Color.FromArgb(125, 145, 162));

            for (int i = 0; i < Items.Count; i++)
            {
                Button button = Items[i];
                if (!button.Visible)
                    continue;

                Color itemBackground = button.IsHovered
                    ? Color.FromArgb(47, 63, 77)
                    : Color.FromArgb(34, 41, 49);
                Color itemBorder = button.IsHovered
                    ? Color.FromArgb(68, 129, 177)
                    : Color.FromArgb(48, 58, 68);

                canvas.DrawFilledRectangle(itemBackground, button.X, button.Y, button.Width, button.Height);
                canvas.DrawRectangle(itemBorder, button.X, button.Y, button.Width, button.Height);

                if (button.IsHovered)
                    canvas.DrawFilledRectangle(accent, button.X, button.Y + 3, 3, button.Height - 6);

                IconManager.DrawScaled(canvas, itemIcons[i], button.X + 12, button.Y + 8, 22, 22);
                DrawItemLabel(canvas, itemLabels[i], button.X + 47, button.Y + 3, button.Width - 57);
            }

            scrollBar.Render(canvas);

            int footerY = Y + Height - FooterHeight;
            canvas.DrawFilledRectangle(footer, X + 1, footerY, Width - 2, FooterHeight - 1);
            canvas.DrawLine(Color.FromArgb(52, 65, 77), X + 1, footerY, X + Width - 2, footerY);
            DrawTinyText(canvas, "ZOnderqOS Gen3", X + 15, footerY + 15, Color.FromArgb(121, 142, 159));
            DrawTinyText(canvas, "ESC  zamknij menu", X + Width - 118, footerY + 15, Color.FromArgb(97, 116, 132));
        }

        private static void DrawItemLabel(Canvas canvas, string text, int x, int y, int availableWidth)
        {
            if (string.IsNullOrEmpty(text) || availableWidth <= 0)
                return;

            const int charWidth = 16;
            int maxChars = Math.Max(1, availableWidth / charWidth);
            string shown = text;
            if (shown.Length > maxChars)
            {
                int take = Math.Max(1, maxChars - 3);
                shown = shown.Substring(0, take) + "...";
            }

            canvas.DrawString(shown, PCScreenFont.DefaultFont, Color.FromArgb(230, 235, 240), x, y);
        }

        private static void DrawTinyText(Canvas canvas, string text, int x, int y, Color color)
        {
            if (string.IsNullOrEmpty(text))
                return;

            for (int i = 0; i < text.Length; i++)
                DrawTinyChar(canvas, text[i], x + i * 6, y, color);
        }

        private static void DrawTinyChar(Canvas canvas, char ch, int x, int y, Color color)
        {
            for (int row = 0; row < 7; row++)
            {
                int bits = TinyGlyph(ch, row);
                for (int col = 0; col < 5; col++)
                {
                    if ((bits & (1 << (4 - col))) != 0)
                        canvas.DrawFilledRectangle(color, x + col, y + row, 1, 1);
                }
            }
        }

        private static int TinyGlyph(char ch, int row)
        {
            string pattern;
            switch (char.ToUpperInvariant(ch))
            {
                case 'A': pattern = "011101000110001111111000110001"; break;
                case 'B': pattern = "111101000110001111101000111101"; break;
                case 'C': pattern = "011101000010000100001000001110"; break;
                case 'D': pattern = "111101000110001100011000111101"; break;
                case 'E': pattern = "111111000010000111101000011111"; break;
                case 'F': pattern = "111111000010000111101000010000"; break;
                case 'G': pattern = "011101000010000101111000101111"; break;
                case 'H': pattern = "100011000110001111111000110001"; break;
                case 'I': pattern = "111110010000100001000010011111"; break;
                case 'J': pattern = "001110001000100001001001001110"; break;
                case 'K': pattern = "100011001010100110001010010001"; break;
                case 'L': pattern = "100001000010000100001000011111"; break;
                case 'M': pattern = "100011101110101101011000110001"; break;
                case 'N': pattern = "100011100110101100111000110001"; break;
                case 'O': pattern = "011101000110001100011000101110"; break;
                case 'P': pattern = "111101000110001111101000010000"; break;
                case 'Q': pattern = "011101000110001100011010010101"; break;
                case 'R': pattern = "111101000110001111101010010001"; break;
                case 'S': pattern = "011111000010000011000000111110"; break;
                case 'T': pattern = "111110010000100001000010000100"; break;
                case 'U': pattern = "100011000110001100011000101110"; break;
                case 'V': pattern = "100011000110001100011010000100"; break;
                case 'W': pattern = "100011000110001101011010101010"; break;
                case 'X': pattern = "100011000101010001000101010001"; break;
                case 'Y': pattern = "100011000101010001000010000100"; break;
                case 'Z': pattern = "111110000100010001000100011111"; break;
                case '0': pattern = "011101000110011101011000101110"; break;
                case '1': pattern = "001000110000100001000010011111"; break;
                case '2': pattern = "011101000100001000100100011111"; break;
                case '3': pattern = "111100000100001001110000111110"; break;
                case '4': pattern = "000100011001010111110001000010"; break;
                case '5': pattern = "111111000011110000010000111110"; break;
                case '6': pattern = "011101000010000111101000101110"; break;
                case '7': pattern = "111110000100010001000010000100"; break;
                case '8': pattern = "011101000110001011101000110111"; break;
                case '9': pattern = "011101000110001011110000101110"; break;
                case '.': pattern = "000000000000000000000000000001"; break;
                case '-': pattern = "000000000000000011100000000000"; break;
                case '_': pattern = "000000000000000000000000011111"; break;
                case ' ': pattern = "000000000000000000000000000000"; break;
                default: pattern = "011101000100010001000000010000"; break;
            }

            int offset = row * 5;
            if (offset + 5 > pattern.Length)
                return 0;

            int bits = 0;
            for (int i = 0; i < 5; i++)
            {
                if (pattern[offset + i] == '1')
                    bits |= 1 << (4 - i);
            }
            return bits;
        }

        public void UpdateInteractions(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            if (!Visible)
                return;

            UpdateLayout();
            if (scrollBar.HandleMouse(mouseX, mouseY, isClicked, wasClicked))
                return;

            foreach (var button in Items)
            {
                if (!button.Visible)
                    continue;

                bool isOver = button.Contains(mouseX, mouseY);
                button.IsHovered = isOver;
                if (isOver && isClicked && !wasClicked)
                    button.InvokeClick();
            }
        }
    }
}
