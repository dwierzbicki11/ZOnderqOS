using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using ZonderqOS.GUI.Apps;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Keyboard-first global launcher backed directly by AppRegistry. The palette owns
    /// only a fixed result-index buffer, so searching does not allocate result lists on
    /// every keystroke.
    /// </summary>
    public sealed class CommandPalette : Widget
    {
        private const int MaxVisibleResults = 6;
        private const int QueryLimit = 48;
        private const int HeaderHeight = 52;
        private const int SearchHeight = 44;
        private const int RowHeight = 48;
        private const int FooterHeight = 34;

        private static readonly Color Surface = Color.FromArgb(20, 25, 31);
        private static readonly Color Header = Color.FromArgb(27, 34, 41);
        private static readonly Color SearchSurface = Color.FromArgb(16, 21, 26);
        private static readonly Color Row = Color.FromArgb(27, 34, 41);
        private static readonly Color RowSelected = Color.FromArgb(38, 57, 73);
        private static readonly Color Border = Color.FromArgb(64, 76, 88);
        private static readonly Color Text = Color.FromArgb(234, 239, 244);
        private static readonly Color Muted = Color.FromArgb(134, 150, 165);

        private readonly AppRegistry registry;
        private readonly int[] resultIndices;
        private string query = string.Empty;
        private int resultCount;
        private int selectedIndex;
        private int firstVisibleIndex;

        public CommandPalette(int screenWidth, int screenHeight, AppRegistry appRegistry)
            : base(0, 0, 680, HeaderHeight + SearchHeight + MaxVisibleResults * RowHeight + FooterHeight + 30)
        {
            registry = appRegistry ?? new AppRegistry();
            resultIndices = new int[System.Math.Max(1, registry.Count)];
            X = System.Math.Max(8, (screenWidth - Width) / 2);
            Y = System.Math.Max(18, System.Math.Min(100, (screenHeight - Height) / 4));
            Visible = false;
            RebuildResults();
        }

        public string Query
        {
            get { return query; }
        }

        public void Show()
        {
            query = string.Empty;
            selectedIndex = 0;
            firstVisibleIndex = 0;
            RebuildResults();
            Visible = true;
        }

        public void Close()
        {
            Visible = false;
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

            if (key.Key == ConsoleKeyEx.Enter || key.Key == ConsoleKeyEx.NumEnter)
            {
                LaunchSelected();
                return true;
            }

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                if (query.Length > 0)
                {
                    query = query.Substring(0, query.Length - 1);
                    selectedIndex = 0;
                    firstVisibleIndex = 0;
                    RebuildResults();
                }
                return true;
            }

            if (key.Key == ConsoleKeyEx.Delete)
            {
                if (query.Length > 0)
                {
                    query = string.Empty;
                    selectedIndex = 0;
                    firstVisibleIndex = 0;
                    RebuildResults();
                }
                return true;
            }

            bool control = (key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control;
            bool alt = (key.Modifiers & ConsoleModifiers.Alt) == ConsoleModifiers.Alt;
            char ch = key.KeyChar;
            if (!control && !alt && ch >= ' ' && ch != 127 && query.Length < QueryLimit)
            {
                query += ch;
                selectedIndex = 0;
                firstVisibleIndex = 0;
                RebuildResults();
                return true;
            }

            return true;
        }

        public bool HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked)
        {
            if (!Visible)
                return false;

            if (!leftClicked || leftWasClicked)
                return true;

            if (!Contains(mouseX, mouseY))
            {
                Close();
                return true;
            }

            int listY = Y + HeaderHeight + SearchHeight + 20;
            if (mouseY >= listY && mouseY < listY + MaxVisibleResults * RowHeight)
            {
                int row = (mouseY - listY) / RowHeight;
                int visibleIndex = firstVisibleIndex + row;
                if (visibleIndex >= 0 && visibleIndex < resultCount)
                {
                    selectedIndex = visibleIndex;
                    LaunchSelected();
                }
            }

            return true;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible || canvas == null)
                return;

            canvas.DrawFilledRectangle(Color.FromArgb(7, 10, 13), X + 8, Y + 9, Width, Height);
            canvas.DrawFilledRectangle(Surface, X, Y, Width, Height);
            canvas.DrawRectangle(SystemTheme.AccentBorder, X, Y, Width, Height);

            RenderHeader(canvas);
            RenderSearch(canvas);
            RenderResults(canvas);
            RenderFooter(canvas);
        }

        private void RenderHeader(Canvas canvas)
        {
            canvas.DrawFilledRectangle(Header, X + 1, Y + 1, Width - 2, HeaderHeight - 1);
            canvas.DrawFilledRectangle(SystemTheme.Accent, X + 1, Y + 1, 4, HeaderHeight - 1);
            IconManager.DrawScaled(canvas, IconType.Search, X + 16, Y + 13, 26, 26);
            SmallTextRenderer.Draw(canvas, "COMMAND PALETTE", X + 54, Y + 14, Text);
            SmallTextRenderer.Draw(canvas, "ALT+SPACE", X + Width - 82, Y + 14, Muted);
            SmallTextRenderer.Draw(canvas, "URUCHOM APLIKACJE PO NAZWIE LUB ID", X + 54, Y + 32, Muted);
        }

        private void RenderSearch(Canvas canvas)
        {
            int x = X + 14;
            int y = Y + HeaderHeight + 10;
            int width = Width - 28;
            canvas.DrawFilledRectangle(SearchSurface, x, y, width, SearchHeight - 8);
            canvas.DrawRectangle(string.IsNullOrEmpty(query) ? Border : SystemTheme.AccentBorder,
                x, y, width, SearchHeight - 8);

            string display = string.IsNullOrEmpty(query) ? "Wpisz np. terminal, settings, disk..." : query;
            Color color = string.IsNullOrEmpty(query) ? Muted : Text;
            SmallTextRenderer.DrawClipped(canvas, display, x + 13, y + 15,
                System.Math.Max(40, width - 42), color);

            if (!string.IsNullOrEmpty(query))
            {
                int caretX = x + 13 + System.Math.Min(SmallTextRenderer.Width(query) + 4, width - 22);
                canvas.DrawFilledRectangle(SystemTheme.Accent, caretX, y + 10, 1, 17);
            }
        }

        private void RenderResults(Canvas canvas)
        {
            int listY = Y + HeaderHeight + SearchHeight + 20;
            if (resultCount == 0)
            {
                SmallTextRenderer.Draw(canvas, "BRAK PASUJACYCH APLIKACJI", X + 20, listY + 18, Muted);
                return;
            }

            for (int row = 0; row < MaxVisibleResults; row++)
            {
                int resultIndex = firstVisibleIndex + row;
                if (resultIndex >= resultCount)
                    break;

                AppDescriptor app = registry.GetAt(resultIndices[resultIndex]);
                if (app == null)
                    continue;

                int y = listY + row * RowHeight;
                bool selected = resultIndex == selectedIndex;
                canvas.DrawFilledRectangle(selected ? RowSelected : Row, X + 14, y, Width - 28, RowHeight - 4);
                canvas.DrawRectangle(selected ? SystemTheme.AccentBorder : Border,
                    X + 14, y, Width - 28, RowHeight - 4);
                if (selected)
                    canvas.DrawFilledRectangle(SystemTheme.Accent, X + 14, y + 5, 3, RowHeight - 14);

                IconManager.DrawScaled(canvas, app.Icon, X + 27, y + 10, 24, 24);
                SmallTextRenderer.DrawClipped(canvas, app.Name, X + 64, y + 10, 250,
                    selected ? Text : Color.FromArgb(218, 225, 232));
                SmallTextRenderer.DrawClipped(canvas, app.Description, X + 64, y + 27, 390, Muted);

                string idText = app.Id ?? string.Empty;
                int idWidth = SmallTextRenderer.Width(idText);
                SmallTextRenderer.DrawClipped(canvas, idText,
                    X + Width - 30 - System.Math.Min(150, idWidth), y + 18, 150,
                    selected ? SystemTheme.Accent : Muted);
            }
        }

        private void RenderFooter(Canvas canvas)
        {
            int y = Y + Height - FooterHeight;
            canvas.DrawLine(Border, X + 1, y, X + Width - 2, y);
            SmallTextRenderer.Draw(canvas, "UP/DOWN WYBOR   ENTER URUCHOM   DELETE WYCZYSC   ESC ZAMKNIJ",
                X + 18, y + 13, Muted);
            SmallTextRenderer.DrawUInt(canvas, (ulong)resultCount, X + Width - 34, y + 13, Text);
        }

        private void MoveSelection(int delta)
        {
            if (resultCount <= 0)
                return;

            selectedIndex += delta;
            if (selectedIndex < 0)
                selectedIndex = resultCount - 1;
            else if (selectedIndex >= resultCount)
                selectedIndex = 0;

            KeepSelectionVisible();
        }

        private void KeepSelectionVisible()
        {
            if (selectedIndex < firstVisibleIndex)
                firstVisibleIndex = selectedIndex;
            else if (selectedIndex >= firstVisibleIndex + MaxVisibleResults)
                firstVisibleIndex = selectedIndex - MaxVisibleResults + 1;

            int maxFirst = System.Math.Max(0, resultCount - MaxVisibleResults);
            if (firstVisibleIndex > maxFirst)
                firstVisibleIndex = maxFirst;
            if (firstVisibleIndex < 0)
                firstVisibleIndex = 0;
        }

        private void LaunchSelected()
        {
            if (selectedIndex < 0 || selectedIndex >= resultCount)
                return;

            AppDescriptor app = registry.GetAt(resultIndices[selectedIndex]);
            if (app == null)
                return;

            string appName = app.Name;
            bool launched = app.Launch();
            if (launched)
            {
                global::ZonderqOS.SystemLogger.Log(global::ZonderqOS.SystemLogLevel.Info,
                    "PALETTE", "Launched application: " + appName);
                Close();
            }
        }

        private void RebuildResults()
        {
            resultCount = 0;
            string trimmed = query == null ? string.Empty : query.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                for (int i = 0; i < registry.Count && resultCount < resultIndices.Length; i++)
                    resultIndices[resultCount++] = i;
                ClampSelection();
                return;
            }

            // Stable multi-pass ranking without temporary collections:
            // exact -> prefix -> name/id contains -> description contains.
            for (int score = 0; score <= 3; score++)
            {
                for (int i = 0; i < registry.Count && resultCount < resultIndices.Length; i++)
                {
                    AppDescriptor app = registry.GetAt(i);
                    if (app != null && MatchScore(app, trimmed) == score)
                        resultIndices[resultCount++] = i;
                }
            }

            ClampSelection();
        }

        private void ClampSelection()
        {
            if (resultCount <= 0)
            {
                selectedIndex = 0;
                firstVisibleIndex = 0;
                return;
            }

            if (selectedIndex >= resultCount)
                selectedIndex = resultCount - 1;
            if (selectedIndex < 0)
                selectedIndex = 0;
            KeepSelectionVisible();
        }

        private static int MatchScore(AppDescriptor app, string value)
        {
            if (app == null || string.IsNullOrEmpty(value))
                return -1;

            if (EqualsIgnoreCase(app.Id, value) || EqualsIgnoreCase(app.Name, value) || EqualsIgnoreCase(app.MenuName, value))
                return 0;

            if (StartsWithIgnoreCase(app.Id, value) || StartsWithIgnoreCase(app.Name, value) ||
                StartsWithIgnoreCase(app.MenuName, value))
                return 1;

            if (ContainsIgnoreCase(app.Id, value) || ContainsIgnoreCase(app.Name, value) ||
                ContainsIgnoreCase(app.MenuName, value))
                return 2;

            if (ContainsIgnoreCase(app.Description, value))
                return 3;

            return -1;
        }

        private static bool EqualsIgnoreCase(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static bool StartsWithIgnoreCase(string text, string value)
        {
            return !string.IsNullOrEmpty(text) &&
                   text.StartsWith(value, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            return !string.IsNullOrEmpty(text) &&
                   text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool Contains(int mouseX, int mouseY)
        {
            return mouseX >= X && mouseX < X + Width && mouseY >= Y && mouseY < Y + Height;
        }
    }
}
