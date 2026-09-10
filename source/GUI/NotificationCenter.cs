using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Right-side notification center with unread/important filters, bounded history,
    /// dismiss actions and a do-not-disturb mode. The panel keeps a fixed index cache
    /// and rebuilds it only when the notification store changes or a filter changes.
    /// </summary>
    public sealed class NotificationCenter : Widget
    {
        private const int VisibleRows = 8;
        private const int CardHeight = 82;
        private const int CardGap = 7;

        private static readonly string[] FilterLabels = { "WSZYSTKIE", "NIEPRZECZYTANE", "WAZNE" };
        private static readonly Color Background = Color.FromArgb(20, 25, 31);
        private static readonly Color Header = Color.FromArgb(27, 34, 42);
        private static readonly Color Panel = Color.FromArgb(30, 37, 45);
        private static readonly Color PanelHover = Color.FromArgb(39, 50, 61);
        private static readonly Color PanelUnread = Color.FromArgb(33, 47, 59);
        private static readonly Color Border = Color.FromArgb(58, 70, 82);
        private static readonly Color Text = Color.FromArgb(233, 238, 243);
        private static readonly Color Muted = Color.FromArgb(133, 149, 163);
        private static readonly Color Good = Color.FromArgb(78, 185, 126);
        private static readonly Color Warning = Color.FromArgb(224, 174, 76);
        private static readonly Color Danger = Color.FromArgb(215, 86, 91);

        private readonly int[] visibleOffsets = new int[NotificationService.Capacity];
        private readonly int screenWidth;
        private readonly int screenHeight;
        private readonly int taskbarHeight;

        private int cachedVersion = -1;
        private int filter;
        private int visibleCount;
        private int selectedIndex;
        private int offset;
        private int hoveredFilter = -1;
        private int hoveredRow = -1;
        private int hoveredDismissRow = -1;
        private int hoveredAction = -1;
        private bool toastHovered;

        public NotificationCenter(int screenWidth, int screenHeight, int taskbarHeight)
            : base(screenWidth - 472, 10, 462, screenHeight - taskbarHeight - 20)
        {
            this.screenWidth = screenWidth;
            this.screenHeight = screenHeight;
            this.taskbarHeight = taskbarHeight;
            Visible = false;
            RebuildFilter();
        }

        public void Toggle()
        {
            Visible = !Visible;
            if (Visible)
            {
                EnsureFresh();
                KeepSelectionVisible();
            }
        }

        public void Close()
        {
            Visible = false;
            hoveredFilter = -1;
            hoveredRow = -1;
            hoveredDismissRow = -1;
            hoveredAction = -1;
        }

        public bool HandleKeyboard(KeyEvent key)
        {
            if (!Visible || key == null)
                return false;

            if (key.Key == ConsoleKeyEx.Escape)
            {
                Close();
                return true;
            }

            if (key.Key == ConsoleKeyEx.LeftArrow)
            {
                filter--;
                if (filter < 0)
                    filter = FilterLabels.Length - 1;
                selectedIndex = 0;
                offset = 0;
                RebuildFilter();
                return true;
            }

            if (key.Key == ConsoleKeyEx.RightArrow)
            {
                filter++;
                if (filter >= FilterLabels.Length)
                    filter = 0;
                selectedIndex = 0;
                offset = 0;
                RebuildFilter();
                return true;
            }

            if (key.Key == ConsoleKeyEx.UpArrow)
            {
                MoveSelection(-1);
                return true;
            }

            if (key.Key == ConsoleKeyEx.DownArrow)
            {
                MoveSelection(1);
                return true;
            }

            if (key.Key == ConsoleKeyEx.Enter)
            {
                MarkSelectedRead();
                return true;
            }

            if (key.Key == ConsoleKeyEx.Delete)
            {
                DismissSelected();
                return true;
            }

            char ch = key.KeyChar;
            if (ch == 'd' || ch == 'D')
            {
                NotificationService.DoNotDisturb = !NotificationService.DoNotDisturb;
                EnsureFresh();
                return true;
            }
            if (ch == 'r' || ch == 'R')
            {
                NotificationService.MarkAllRead();
                EnsureFresh();
                return true;
            }
            if (ch == 'c' || ch == 'C')
            {
                NotificationService.ClearAll();
                EnsureFresh();
                return true;
            }

            return true;
        }

        /// <summary>
        /// Returns true when the panel/toast owns the pointer event. This prevents
        /// clicks from falling through onto applications behind the overlay.
        /// </summary>
        public bool UpdateInteractions(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            toastHovered = false;

            if (!Visible)
            {
                NotificationEntry toast;
                if (!NotificationService.TryGetActiveToast(out toast))
                    return false;

                int toastX = screenWidth - 432;
                int toastY = screenHeight - taskbarHeight - 116;
                toastHovered = Hit(mouseX, mouseY, toastX, toastY, 414, 98);
                if (toastHovered && isClicked && !wasClicked)
                {
                    Visible = true;
                    NotificationService.MarkRead(toast.Id);
                    EnsureFresh();
                    return true;
                }
                return toastHovered;
            }

            EnsureFresh();
            bool inside = Hit(mouseX, mouseY, X, Y, Width, Height);
            if (!inside)
            {
                if (isClicked && !wasClicked)
                {
                    Close();
                    return true;
                }
                return false;
            }

            UpdateHover(mouseX, mouseY);
            if (!isClicked || wasClicked)
                return true;

            if (hoveredFilter >= 0)
            {
                filter = hoveredFilter;
                selectedIndex = 0;
                offset = 0;
                RebuildFilter();
                return true;
            }

            if (hoveredDismissRow >= 0)
            {
                int visibleIndex = offset + hoveredDismissRow;
                if (visibleIndex >= 0 && visibleIndex < visibleCount)
                {
                    NotificationEntry entry;
                    if (TryGetVisible(visibleIndex, out entry))
                        NotificationService.Dismiss(entry.Id);
                    EnsureFresh();
                }
                return true;
            }

            if (hoveredRow >= 0)
            {
                int visibleIndex = offset + hoveredRow;
                if (visibleIndex >= 0 && visibleIndex < visibleCount)
                {
                    selectedIndex = visibleIndex;
                    NotificationEntry entry;
                    if (TryGetVisible(visibleIndex, out entry))
                        NotificationService.MarkRead(entry.Id);
                    EnsureFresh();
                    KeepSelectionVisible();
                }
                return true;
            }

            if (hoveredAction == 0)
            {
                NotificationService.MarkAllRead();
                EnsureFresh();
            }
            else if (hoveredAction == 1)
            {
                NotificationService.ClearAll();
                EnsureFresh();
            }
            else if (hoveredAction == 2)
            {
                NotificationService.DoNotDisturb = !NotificationService.DoNotDisturb;
                EnsureFresh();
            }

            return true;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible)
            {
                RenderToast(canvas);
                return;
            }

            EnsureFresh();
            canvas.DrawFilledRectangle(Color.FromArgb(10, 13, 17), X + 6, Y + 7, Width, Height);
            canvas.DrawFilledRectangle(Background, X, Y, Width, Height);
            canvas.DrawRectangle(Border, X, Y, Width, Height);

            RenderHeader(canvas);
            RenderFilters(canvas);
            RenderList(canvas);
            RenderFooter(canvas);
        }

        private void RenderHeader(Canvas canvas)
        {
            canvas.DrawFilledRectangle(Header, X + 1, Y + 1, Width - 2, 72);
            canvas.DrawFilledRectangle(SystemTheme.Accent, X + 1, Y + 1, 4, 72);
            IconManager.DrawScaled(canvas, IconType.About, X + 18, Y + 19, 28, 28);
            SmallTextRenderer.Draw(canvas, "CENTRUM POWIADOMIEN", X + 58, Y + 20, Text);
            SmallTextRenderer.Draw(canvas, "CTRL+ALT+N  |  D DND  |  R PRZECZYTANE", X + 58, Y + 41, Muted);

            int unread = NotificationService.UnreadCount;
            int badgeX = X + Width - 82;
            canvas.DrawFilledRectangle(unread > 0 ? SystemTheme.AccentSoft : Panel, badgeX, Y + 17, 60, 36);
            canvas.DrawRectangle(unread > 0 ? SystemTheme.AccentBorder : Border, badgeX, Y + 17, 60, 36);
            if (unread > 99)
                SmallTextRenderer.DrawCentered(canvas, "99+", badgeX + 4, Y + 31, 52, Text);
            else
            {
                int width = SmallTextRenderer.WidthUInt((ulong)unread);
                SmallTextRenderer.DrawUInt(canvas, (ulong)unread, badgeX + (60 - width) / 2, Y + 31,
                    unread > 0 ? Text : Muted);
            }
        }

        private void RenderFilters(Canvas canvas)
        {
            int y = Y + 82;
            int gap = 7;
            int totalWidth = Width - 28;
            int tabWidth = (totalWidth - gap * 2) / 3;
            int startX = X + 14;

            for (int i = 0; i < FilterLabels.Length; i++)
            {
                int tx = startX + i * (tabWidth + gap);
                bool selected = filter == i;
                bool hovered = hoveredFilter == i;
                canvas.DrawFilledRectangle(selected ? PanelUnread : hovered ? PanelHover : Panel,
                    tx, y, tabWidth, 36);
                canvas.DrawRectangle(selected || hovered ? SystemTheme.AccentBorder : Border,
                    tx, y, tabWidth, 36);
                if (selected)
                    canvas.DrawFilledRectangle(SystemTheme.Accent, tx, y + 33, tabWidth, 3);
                SmallTextRenderer.DrawCentered(canvas, FilterLabels[i], tx + 4, y + 15,
                    tabWidth - 8, selected ? Text : Muted);
            }
        }

        private void RenderList(Canvas canvas)
        {
            int startY = Y + 130;
            int cardWidth = Width - 28;
            int cardX = X + 14;

            for (int row = 0; row < VisibleRows; row++)
            {
                int visibleIndex = offset + row;
                int cy = startY + row * (CardHeight + CardGap);
                if (cy + CardHeight > Y + Height - 72)
                    break;

                if (visibleIndex >= visibleCount)
                {
                    if (row == 0 && visibleCount == 0)
                    {
                        canvas.DrawFilledRectangle(Panel, cardX, cy, cardWidth, 92);
                        canvas.DrawRectangle(Border, cardX, cy, cardWidth, 92);
                        IconManager.DrawScaled(canvas, IconType.About, cardX + 18, cy + 27, 28, 28);
                        SmallTextRenderer.Draw(canvas, "BRAK POWIADOMIEN", cardX + 60, cy + 29, Text);
                        SmallTextRenderer.Draw(canvas, "System nie ma nic nowego do pokazania.", cardX + 60, cy + 50, Muted);
                    }
                    continue;
                }

                NotificationEntry entry;
                if (!TryGetVisible(visibleIndex, out entry))
                    continue;

                bool selected = visibleIndex == selectedIndex;
                bool hovered = row == hoveredRow;
                bool unread = !entry.IsRead;
                Color background = unread ? PanelUnread : hovered ? PanelHover : Panel;
                if (selected)
                    background = Color.FromArgb(38, 54, 66);

                canvas.DrawFilledRectangle(background, cardX, cy, cardWidth, CardHeight);
                canvas.DrawRectangle(selected || hovered ? SystemTheme.AccentBorder : Border,
                    cardX, cy, cardWidth, CardHeight);
                Color kindColor = KindColor(entry.Kind);
                canvas.DrawFilledRectangle(kindColor, cardX, cy + 6, 3, CardHeight - 12);
                IconManager.DrawScaled(canvas, KindIcon(entry.Kind), cardX + 14, cy + 15, 24, 24);

                SmallTextRenderer.DrawClipped(canvas, entry.Title, cardX + 50, cy + 13,
                    cardWidth - 118, unread ? Text : Color.FromArgb(194, 204, 213));
                SmallTextRenderer.DrawClipped(canvas, entry.Message, cardX + 50, cy + 34,
                    cardWidth - 68, Muted);
                SmallTextRenderer.DrawClipped(canvas, entry.Source, cardX + 50, cy + 56,
                    cardWidth - 155, kindColor);
                SmallTextRenderer.Draw(canvas, entry.TimeText, cardX + cardWidth - 86, cy + 56, Muted);

                int dismissX = cardX + cardWidth - 35;
                int dismissY = cy + 9;
                bool dismissHover = hoveredDismissRow == row;
                canvas.DrawFilledRectangle(dismissHover ? Color.FromArgb(68, 43, 48) : Color.FromArgb(34, 40, 47),
                    dismissX, dismissY, 24, 24);
                canvas.DrawRectangle(dismissHover ? Danger : Border, dismissX, dismissY, 24, 24);
                IconManager.DrawScaled(canvas, IconType.Close, dismissX + 5, dismissY + 5, 14, 14);

                if (unread)
                    canvas.DrawFilledRectangle(SystemTheme.Accent, cardX + cardWidth - 12, cy + 39, 5, 5);
            }
        }

        private void RenderFooter(Canvas canvas)
        {
            int y = Y + Height - 57;
            canvas.DrawFilledRectangle(Header, X + 1, y, Width - 2, 56);
            canvas.DrawLine(Border, X + 1, y, X + Width - 2, y);

            DrawFooterButton(canvas, 0, X + 14, y + 9, 126, "PRZECZYTANE", IconType.About);
            DrawFooterButton(canvas, 1, X + 147, y + 9, 104, "WYCZYSC", IconType.Trash);
            DrawFooterButton(canvas, 2, X + 258, y + 9, Width - 272,
                NotificationService.DoNotDisturb ? "DND: WL." : "DND: WYL.", IconType.Settings);
        }

        private void DrawFooterButton(Canvas canvas, int index, int x, int y, int width, string label, IconType icon)
        {
            bool hovered = hoveredAction == index;
            bool dndActive = index == 2 && NotificationService.DoNotDisturb;
            canvas.DrawFilledRectangle(dndActive ? Color.FromArgb(58, 47, 32) : hovered ? PanelHover : Panel,
                x, y, width, 38);
            canvas.DrawRectangle(dndActive ? Warning : hovered ? SystemTheme.AccentBorder : Border,
                x, y, width, 38);
            IconManager.DrawScaled(canvas, icon, x + 9, y + 10, 18, 18);
            SmallTextRenderer.DrawClipped(canvas, label, x + 35, y + 16,
                System.Math.Max(20, width - 43), dndActive ? Warning : Text);
        }

        private void RenderToast(Canvas canvas)
        {
            NotificationEntry entry;
            if (!NotificationService.TryGetActiveToast(out entry))
                return;

            int x = screenWidth - 432;
            int y = screenHeight - taskbarHeight - 116;
            const int width = 414;
            const int height = 98;
            Color kindColor = KindColor(entry.Kind);

            canvas.DrawFilledRectangle(Color.FromArgb(9, 12, 15), x + 5, y + 6, width, height);
            canvas.DrawFilledRectangle(toastHovered ? PanelHover : Header, x, y, width, height);
            canvas.DrawRectangle(toastHovered ? SystemTheme.AccentBorder : Border, x, y, width, height);
            canvas.DrawFilledRectangle(kindColor, x, y + 7, 4, height - 14);
            IconManager.DrawScaled(canvas, KindIcon(entry.Kind), x + 16, y + 17, 28, 28);
            SmallTextRenderer.DrawClipped(canvas, entry.Title, x + 57, y + 17, width - 92, Text);
            SmallTextRenderer.DrawClipped(canvas, entry.Message, x + 57, y + 40, width - 75, Muted);
            SmallTextRenderer.DrawClipped(canvas, entry.Source, x + 57, y + 66, width - 150, kindColor);
            SmallTextRenderer.Draw(canvas, entry.TimeText, x + width - 71, y + 66, Muted);
        }

        private void UpdateHover(int mouseX, int mouseY)
        {
            hoveredFilter = -1;
            hoveredRow = -1;
            hoveredDismissRow = -1;
            hoveredAction = -1;

            int filterY = Y + 82;
            int gap = 7;
            int totalWidth = Width - 28;
            int tabWidth = (totalWidth - gap * 2) / 3;
            int filterX = X + 14;
            for (int i = 0; i < 3; i++)
            {
                int tx = filterX + i * (tabWidth + gap);
                if (Hit(mouseX, mouseY, tx, filterY, tabWidth, 36))
                {
                    hoveredFilter = i;
                    return;
                }
            }

            int startY = Y + 130;
            int cardWidth = Width - 28;
            int cardX = X + 14;
            for (int row = 0; row < VisibleRows; row++)
            {
                int visibleIndex = offset + row;
                if (visibleIndex >= visibleCount)
                    break;
                int cy = startY + row * (CardHeight + CardGap);
                if (cy + CardHeight > Y + Height - 72)
                    break;

                if (Hit(mouseX, mouseY, cardX + cardWidth - 35, cy + 9, 24, 24))
                {
                    hoveredDismissRow = row;
                    hoveredRow = row;
                    return;
                }
                if (Hit(mouseX, mouseY, cardX, cy, cardWidth, CardHeight))
                {
                    hoveredRow = row;
                    return;
                }
            }

            int footerY = Y + Height - 48;
            if (Hit(mouseX, mouseY, X + 14, footerY, 126, 38))
                hoveredAction = 0;
            else if (Hit(mouseX, mouseY, X + 147, footerY, 104, 38))
                hoveredAction = 1;
            else if (Hit(mouseX, mouseY, X + 258, footerY, Width - 272, 38))
                hoveredAction = 2;
        }

        private void EnsureFresh()
        {
            if (cachedVersion != NotificationService.Version)
                RebuildFilter();
        }

        private void RebuildFilter()
        {
            cachedVersion = NotificationService.Version;
            visibleCount = 0;
            for (int newestOffset = 0; newestOffset < NotificationService.Count; newestOffset++)
            {
                NotificationEntry entry;
                if (!NotificationService.TryGetNewest(newestOffset, out entry) || entry.IsDismissed)
                    continue;
                if (filter == 1 && entry.IsRead)
                    continue;
                if (filter == 2 && !IsImportant(entry.Kind))
                    continue;

                if (visibleCount < visibleOffsets.Length)
                    visibleOffsets[visibleCount++] = newestOffset;
            }

            if (visibleCount <= 0)
            {
                selectedIndex = 0;
                offset = 0;
                return;
            }

            if (selectedIndex >= visibleCount)
                selectedIndex = visibleCount - 1;
            if (selectedIndex < 0)
                selectedIndex = 0;
            KeepSelectionVisible();
        }

        private bool TryGetVisible(int visibleIndex, out NotificationEntry entry)
        {
            entry = default(NotificationEntry);
            if (visibleIndex < 0 || visibleIndex >= visibleCount)
                return false;
            return NotificationService.TryGetNewest(visibleOffsets[visibleIndex], out entry);
        }

        private void MoveSelection(int delta)
        {
            EnsureFresh();
            if (visibleCount <= 0)
                return;
            selectedIndex += delta;
            if (selectedIndex < 0)
                selectedIndex = visibleCount - 1;
            else if (selectedIndex >= visibleCount)
                selectedIndex = 0;
            KeepSelectionVisible();
        }

        private void KeepSelectionVisible()
        {
            if (selectedIndex < offset)
                offset = selectedIndex;
            else if (selectedIndex >= offset + VisibleRows)
                offset = selectedIndex - VisibleRows + 1;

            int maxOffset = System.Math.Max(0, visibleCount - VisibleRows);
            if (offset > maxOffset)
                offset = maxOffset;
            if (offset < 0)
                offset = 0;
        }

        private void MarkSelectedRead()
        {
            NotificationEntry entry;
            if (TryGetVisible(selectedIndex, out entry))
            {
                NotificationService.MarkRead(entry.Id);
                EnsureFresh();
            }
        }

        private void DismissSelected()
        {
            NotificationEntry entry;
            if (TryGetVisible(selectedIndex, out entry))
            {
                NotificationService.Dismiss(entry.Id);
                EnsureFresh();
            }
        }

        private static bool IsImportant(NotificationKind kind)
        {
            return kind == NotificationKind.Warning || kind == NotificationKind.Error ||
                   kind == NotificationKind.Security;
        }

        private static Color KindColor(NotificationKind kind)
        {
            if (kind == NotificationKind.Success)
                return Good;
            if (kind == NotificationKind.Warning)
                return Warning;
            if (kind == NotificationKind.Error)
                return Danger;
            if (kind == NotificationKind.Security)
                return Color.FromArgb(170, 128, 224);
            if (kind == NotificationKind.Network)
                return Color.FromArgb(74, 166, 218);
            if (kind == NotificationKind.Storage)
                return Color.FromArgb(106, 179, 140);
            return SystemTheme.Accent;
        }

        private static IconType KindIcon(NotificationKind kind)
        {
            if (kind == NotificationKind.Network)
                return IconType.Network;
            if (kind == NotificationKind.Storage)
                return IconType.DiskManager;
            if (kind == NotificationKind.Error)
                return IconType.Close;
            if (kind == NotificationKind.Security)
                return IconType.Settings;
            return IconType.About;
        }

        private static bool Hit(int px, int py, int x, int y, int width, int height)
        {
            return px >= x && px < x + width && py >= y && py < y + height;
        }
    }
}
