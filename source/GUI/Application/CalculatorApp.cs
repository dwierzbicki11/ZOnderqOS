using System;
using System.Drawing;
using System.Globalization;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Native desktop calculator. Rendering is allocation-free while idle: all button
    /// labels are static and display/history strings change only after user input.
    /// </summary>
    public sealed class CalculatorApp : Application
    {
        private const int Columns = 4;
        private const int Rows = 7;
        private const int SideMargin = 16;
        private const int ButtonGap = 7;
        private const int GridTopOffset = 236;

        private static readonly string[] ButtonLabels =
        {
            "MC", "MR", "M+", "M-",
            "%", "1/x", "x2", "SQRT",
            "C", "CE", "BACK", "/",
            "7", "8", "9", "*",
            "4", "5", "6", "-",
            "1", "2", "3", "+",
            "+/-", "0", ".", "="
        };

        private static readonly Color Surface = Color.FromArgb(20, 25, 31);
        private static readonly Color Panel = Color.FromArgb(27, 34, 41);
        private static readonly Color ButtonNormal = Color.FromArgb(34, 42, 50);
        private static readonly Color ButtonHover = Color.FromArgb(44, 57, 69);
        private static readonly Color FunctionButton = Color.FromArgb(31, 39, 47);
        private static readonly Color OperatorButton = Color.FromArgb(31, 49, 63);
        private static readonly Color ClearButton = Color.FromArgb(58, 40, 44);
        private static readonly Color Border = Color.FromArgb(57, 70, 82);
        private static readonly Color Text = Color.FromArgb(234, 239, 244);
        private static readonly Color Muted = Color.FromArgb(132, 149, 164);

        private readonly Action closeCallback;
        private string entry = "0";
        private string expression = string.Empty;
        private string history0 = string.Empty;
        private string history1 = string.Empty;
        private string history2 = string.Empty;
        private string errorMessage = string.Empty;
        private double accumulator;
        private double memory;
        private char pendingOperator;
        private bool replaceEntry = true;
        private bool errorState;
        private int hoveredButton = -1;

        public CalculatorApp(int x, int y, Action onClose) : base("Kalkulator")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 500, 690, "Kalkulator");
            Window.CloseAction = Close;
        }

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
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

            if (key.Key == ConsoleKeyEx.Enter)
            {
                PressButton(27);
                return;
            }

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                PressButton(10);
                return;
            }

            if (key.Key == ConsoleKeyEx.Delete)
            {
                PressButton(9);
                return;
            }

            char ch = key.KeyChar;
            if (ch >= '0' && ch <= '9')
            {
                PressDigit(ch);
                return;
            }

            if (ch == '.' || ch == ',')
            {
                PressDecimal();
                return;
            }

            if (ch == '+' || ch == '-' || ch == '*' || ch == '/')
            {
                SetBinaryOperator(ch);
                return;
            }

            if (ch == '=' )
            {
                PressButton(27);
                return;
            }

            if (ch == '%')
                ApplyPercent();
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            Window.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked);
            if (!Window.Visible || Window.IsMinimized)
            {
                hoveredButton = -1;
                return;
            }

            hoveredButton = HitButton(mouseX, mouseY);
            if (hoveredButton >= 0 && leftClicked && !leftWasClicked)
                PressButton(hoveredButton);
        }

        public override void Render(Canvas canvas)
        {
            if (!Window.Visible || Window.IsMinimized)
                return;

            base.Render(canvas);

            int contentX = Window.X + 12;
            int contentY = Window.Y + 43;
            int contentWidth = System.Math.Max(320, Window.Width - 24);

            canvas.DrawFilledRectangle(Surface, contentX, contentY, contentWidth,
                System.Math.Max(120, Window.Height - 55));

            IconManager.DrawScaled(canvas, IconType.Calculator, contentX + 8, contentY + 8, 28, 28);
            SmallTextRenderer.Draw(canvas, "KALKULATOR STANDARDOWY", contentX + 46, contentY + 12, Text);
            SmallTextRenderer.Draw(canvas, "KLAWIATURA I MYSZ", contentX + 46, contentY + 28, Muted);

            RenderHistory(canvas, contentX, contentY, contentWidth);
            RenderDisplay(canvas, contentX, contentY, contentWidth);
            RenderButtons(canvas);
        }

        private void RenderHistory(Canvas canvas, int x, int y, int width)
        {
            int historyX = x + 8;
            int historyWidth = System.Math.Max(40, width - 16);
            if (!string.IsNullOrEmpty(history2))
                SmallTextRenderer.DrawClipped(canvas, history2, historyX, y + 49, historyWidth, Muted);
            if (!string.IsNullOrEmpty(history1))
                SmallTextRenderer.DrawClipped(canvas, history1, historyX, y + 64, historyWidth, Muted);
            if (!string.IsNullOrEmpty(history0))
                SmallTextRenderer.DrawClipped(canvas, history0, historyX, y + 79, historyWidth, Muted);
        }

        private void RenderDisplay(Canvas canvas, int x, int y, int width)
        {
            int displayX = x + 4;
            int displayY = y + 98;
            int displayWidth = System.Math.Max(100, width - 8);
            canvas.DrawFilledRectangle(Panel, displayX, displayY, displayWidth, 86);
            canvas.DrawRectangle(errorState ? Color.FromArgb(135, 67, 76) : SystemTheme.AccentBorder,
                displayX, displayY, displayWidth, 86);

            if (memory != 0.0)
            {
                canvas.DrawFilledRectangle(SystemTheme.AccentSoft, displayX + 9, displayY + 9, 19, 18);
                SmallTextRenderer.DrawCentered(canvas, "M", displayX + 9, displayY + 14, 19, Text);
            }

            if (!string.IsNullOrEmpty(expression))
                SmallTextRenderer.DrawClipped(canvas, expression, displayX + 36, displayY + 15,
                    System.Math.Max(30, displayWidth - 48), Muted);

            string displayText = errorState ? errorMessage : entry;
            int textWidth = SmallTextRenderer.Width(displayText);
            int textX = displayX + displayWidth - 12 - textWidth;
            if (textX < displayX + 10)
                textX = displayX + 10;
            SmallTextRenderer.DrawClipped(canvas, displayText, textX, displayY + 54,
                System.Math.Max(20, displayX + displayWidth - 10 - textX), Text);
        }

        private void RenderButtons(Canvas canvas)
        {
            int gridX;
            int gridY;
            int buttonWidth;
            int buttonHeight;
            GetGrid(out gridX, out gridY, out buttonWidth, out buttonHeight);

            for (int index = 0; index < ButtonLabels.Length; index++)
            {
                int row = index / Columns;
                int column = index % Columns;
                int x = gridX + column * (buttonWidth + ButtonGap);
                int y = gridY + row * (buttonHeight + ButtonGap);
                bool hovered = index == hoveredButton;

                Color background = GetButtonBackground(index, hovered);
                Color border = hovered ? SystemTheme.AccentBorder : Border;
                if (index == 27)
                {
                    background = hovered ? SystemTheme.AccentBorder : SystemTheme.Accent;
                    border = SystemTheme.AccentBorder;
                }

                canvas.DrawFilledRectangle(background, x, y, buttonWidth, buttonHeight);
                canvas.DrawRectangle(border, x, y, buttonWidth, buttonHeight);
                if (hovered)
                    canvas.DrawFilledRectangle(SystemTheme.Accent, x, y, buttonWidth, 2);

                SmallTextRenderer.DrawCentered(canvas, ButtonLabels[index], x + 4,
                    y + (buttonHeight / 2) - 3, System.Math.Max(20, buttonWidth - 8), Text);
            }
        }

        private static Color GetButtonBackground(int index, bool hovered)
        {
            if (hovered)
                return ButtonHover;
            if (index >= 8 && index <= 10)
                return ClearButton;
            if (index == 11 || index == 15 || index == 19 || index == 23)
                return OperatorButton;
            if (index < 12 || index == 24 || index == 26)
                return FunctionButton;
            return ButtonNormal;
        }

        private void GetGrid(out int gridX, out int gridY, out int buttonWidth, out int buttonHeight)
        {
            gridX = Window.X + SideMargin;
            gridY = Window.Y + GridTopOffset;
            int availableWidth = System.Math.Max(300, Window.Width - SideMargin * 2 - ButtonGap * (Columns - 1));
            buttonWidth = System.Math.Max(68, availableWidth / Columns);

            int availableHeight = System.Math.Max(308,
                Window.Height - GridTopOffset - 16 - ButtonGap * (Rows - 1));
            buttonHeight = availableHeight / Rows;
            buttonHeight = System.Math.Max(42, System.Math.Min(58, buttonHeight));
        }

        private int HitButton(int mouseX, int mouseY)
        {
            int gridX;
            int gridY;
            int buttonWidth;
            int buttonHeight;
            GetGrid(out gridX, out gridY, out buttonWidth, out buttonHeight);

            int relativeX = mouseX - gridX;
            int relativeY = mouseY - gridY;
            if (relativeX < 0 || relativeY < 0)
                return -1;

            int strideX = buttonWidth + ButtonGap;
            int strideY = buttonHeight + ButtonGap;
            int column = relativeX / strideX;
            int row = relativeY / strideY;
            if (column < 0 || column >= Columns || row < 0 || row >= Rows)
                return -1;
            if (relativeX % strideX >= buttonWidth || relativeY % strideY >= buttonHeight)
                return -1;

            int index = row * Columns + column;
            return index < ButtonLabels.Length ? index : -1;
        }

        private void PressButton(int index)
        {
            if (index < 0 || index >= ButtonLabels.Length)
                return;

            switch (index)
            {
                case 0: memory = 0.0; return;
                case 1: RecallMemory(); return;
                case 2: AddToMemory(1.0); return;
                case 3: AddToMemory(-1.0); return;
                case 4: ApplyPercent(); return;
                case 5: ApplyUnary(1); return;
                case 6: ApplyUnary(2); return;
                case 7: ApplyUnary(3); return;
                case 8: ClearAll(); return;
                case 9: ClearEntry(); return;
                case 10: Backspace(); return;
                case 11: SetBinaryOperator('/'); return;
                case 12: PressDigit('7'); return;
                case 13: PressDigit('8'); return;
                case 14: PressDigit('9'); return;
                case 15: SetBinaryOperator('*'); return;
                case 16: PressDigit('4'); return;
                case 17: PressDigit('5'); return;
                case 18: PressDigit('6'); return;
                case 19: SetBinaryOperator('-'); return;
                case 20: PressDigit('1'); return;
                case 21: PressDigit('2'); return;
                case 22: PressDigit('3'); return;
                case 23: SetBinaryOperator('+'); return;
                case 24: ToggleSign(); return;
                case 25: PressDigit('0'); return;
                case 26: PressDecimal(); return;
                case 27: CalculateEquals(); return;
            }
        }

        private void PressDigit(char digit)
        {
            RecoverFromError();
            if (replaceEntry || entry == "0")
            {
                entry = digit.ToString();
                replaceEntry = false;
                return;
            }

            if (entry.Length < 18)
                entry += digit;
        }

        private void PressDecimal()
        {
            RecoverFromError();
            if (replaceEntry)
            {
                entry = "0.";
                replaceEntry = false;
                return;
            }

            if (entry.IndexOf('.') < 0 && entry.Length < 18)
                entry += ".";
        }

        private void ToggleSign()
        {
            RecoverFromError();
            if (entry == "0")
                return;

            if (entry.StartsWith("-"))
                entry = entry.Substring(1);
            else
                entry = "-" + entry;
            replaceEntry = false;
        }

        private void Backspace()
        {
            if (errorState || replaceEntry)
                return;

            if (entry.Length <= 1 || (entry.Length == 2 && entry[0] == '-'))
                entry = "0";
            else
                entry = entry.Substring(0, entry.Length - 1);
        }

        private void ClearEntry()
        {
            errorState = false;
            errorMessage = string.Empty;
            entry = "0";
            replaceEntry = true;
        }

        private void ClearAll()
        {
            errorState = false;
            errorMessage = string.Empty;
            entry = "0";
            expression = string.Empty;
            accumulator = 0.0;
            pendingOperator = '\0';
            replaceEntry = true;
        }

        private void RecallMemory()
        {
            errorState = false;
            errorMessage = string.Empty;
            entry = FormatNumber(memory);
            replaceEntry = true;
        }

        private void AddToMemory(double direction)
        {
            double value;
            if (!TryGetEntry(out value))
                return;
            memory += value * direction;
        }

        private void ApplyPercent()
        {
            double value;
            if (!TryGetEntry(out value))
                return;
            SetEntryValue(value / 100.0, "%");
        }

        private void ApplyUnary(int operation)
        {
            double value;
            if (!TryGetEntry(out value))
                return;

            double result;
            string symbol;
            if (operation == 1)
            {
                if (value == 0.0)
                {
                    SetError("DZIELENIE PRZEZ ZERO");
                    return;
                }
                result = 1.0 / value;
                symbol = "1/x";
            }
            else if (operation == 2)
            {
                result = value * value;
                symbol = "x2";
            }
            else
            {
                if (value < 0.0)
                {
                    SetError("UJEMNY PIERWIASTEK");
                    return;
                }
                result = System.Math.Sqrt(value);
                symbol = "SQRT";
            }

            SetEntryValue(result, symbol);
        }

        private void SetEntryValue(double value, string symbol)
        {
            if (!IsFinite(value))
            {
                SetError("WYNIK POZA ZAKRESEM");
                return;
            }

            string oldEntry = entry;
            entry = FormatNumber(value);
            PushHistory(symbol + "(" + oldEntry + ") = " + entry);
            replaceEntry = true;
            errorState = false;
            errorMessage = string.Empty;
        }

        private void SetBinaryOperator(char operation)
        {
            double current;
            if (!TryGetEntry(out current))
                return;

            if (pendingOperator != '\0' && !replaceEntry)
            {
                double result;
                if (!TryApplyBinary(accumulator, current, pendingOperator, out result))
                    return;
                accumulator = result;
                entry = FormatNumber(result);
            }
            else if (pendingOperator == '\0')
            {
                accumulator = current;
            }

            pendingOperator = operation;
            expression = entry + " " + operation;
            replaceEntry = true;
        }

        private void CalculateEquals()
        {
            if (pendingOperator == '\0')
                return;

            double right;
            if (!TryGetEntry(out right))
                return;

            char operation = pendingOperator;
            double left = accumulator;
            double result;
            if (!TryApplyBinary(left, right, operation, out result))
                return;

            string leftText = FormatNumber(left);
            string rightText = FormatNumber(right);
            entry = FormatNumber(result);
            PushHistory(leftText + " " + operation + " " + rightText + " = " + entry);
            expression = string.Empty;
            pendingOperator = '\0';
            accumulator = result;
            replaceEntry = true;
        }

        private bool TryApplyBinary(double left, double right, char operation, out double result)
        {
            result = 0.0;
            if (operation == '+') result = left + right;
            else if (operation == '-') result = left - right;
            else if (operation == '*') result = left * right;
            else if (operation == '/')
            {
                if (right == 0.0)
                {
                    SetError("DZIELENIE PRZEZ ZERO");
                    return false;
                }
                result = left / right;
            }
            else return false;

            if (!IsFinite(result))
            {
                SetError("WYNIK POZA ZAKRESEM");
                return false;
            }
            return true;
        }

        private bool TryGetEntry(out double value)
        {
            value = 0.0;
            if (errorState)
                return false;

            if (!double.TryParse(entry, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                SetError("NIEPRAWIDLOWA LICZBA");
                return false;
            }
            return true;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string FormatNumber(double value)
        {
            if (value == 0.0)
                return "0";
            return value.ToString("G14", CultureInfo.InvariantCulture);
        }

        private void PushHistory(string text)
        {
            history2 = history1;
            history1 = history0;
            history0 = text ?? string.Empty;
        }

        private void SetError(string message)
        {
            errorState = true;
            errorMessage = message ?? "BLAD";
            expression = string.Empty;
            pendingOperator = '\0';
            accumulator = 0.0;
            replaceEntry = true;
        }

        private void RecoverFromError()
        {
            if (!errorState)
                return;
            errorState = false;
            errorMessage = string.Empty;
            entry = "0";
            expression = string.Empty;
            pendingOperator = '\0';
            accumulator = 0.0;
            replaceEntry = true;
        }
    }
}
