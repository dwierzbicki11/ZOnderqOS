using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class BasicIdeApp : Application
    {
        private static readonly Color Surface = Color.FromArgb(20, 24, 30);
        private static readonly Color ToolbarBg = Color.FromArgb(28, 34, 44);
        private static readonly Color EditorBg = Color.FromArgb(16, 20, 26);
        private static readonly Color ConsoleBg = Color.FromArgb(10, 14, 18);
        private static readonly Color Border = Color.FromArgb(50, 64, 78);
        private static readonly Color TextColor = Color.FromArgb(230, 236, 242);
        private static readonly Color MutedColor = Color.FromArgb(120, 138, 154);
        private static readonly Color AccentColor = Color.FromArgb(70, 160, 225);
        private static readonly Color GreenColor = Color.FromArgb(55, 195, 95);
        private static readonly Color RedColor = Color.FromArgb(235, 75, 75);
        private static readonly Color YellowColor = Color.FromArgb(240, 195, 45);
        private static readonly Color TerminalGreen = Color.FromArgb(80, 235, 110);
        private static readonly Color LineNumColor = Color.FromArgb(75, 95, 115);
        private static readonly Color ActiveLineBg = Color.FromArgb(28, 36, 48);

        // Editor state
        private readonly List<string> sourceLines = new List<string>(64);
        private int cursorRow = 0;
        private int cursorCol = 0;
        private int scrollRow = 0;
        private int blinkCounter = 0;

        // Console output state
        private readonly List<string> consoleLines = new List<string>(128);
        private int consoleScrollRow = 0;

        // Interpreter state
        private bool isRunning;
        private bool isPaused;
        private long resumeTimestamp;
        private long toneEndTimestamp;
        private string statusMessage = "Gotowy do uruchomienia";
        private Color statusColor = MutedColor;

        private readonly Action closeCallback;

        // Execution engine
        private struct ParsedLine
        {
            public int LineNum;
            public string Statement;
            public int SourceIndex;
        }

        private struct ForState
        {
            public string VarName;
            public int Target;
            public int Step;
            public int ProgramCounter;
        }

        private readonly List<ParsedLine> program = new List<ParsedLine>(64);
        private readonly Dictionary<string, int> variables = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Stack<int> callStack = new Stack<int>(16);
        private readonly Stack<ForState> forStack = new Stack<ForState>(8);
        private int pc = 0; // Program counter index in 'program'
        private int sampleIndex = 0;

        private static readonly string[][] SamplePrograms = new string[][]
        {
            // Sample 0: PC Speaker Melodia & Chiptune
            new string[]
            {
                "10 REM --- MELODIA PC SPEAKER ---",
                "20 CLS",
                "30 PRINT \"ZONDERQ CHIPTUNE DEMO\"",
                "40 PRINT \"---------------------\"",
                "50 FOR F = 250 TO 650 STEP 80",
                "60 PRINT \"TON: \"; F; \" HZ\"",
                "70 BEEP F, 70",
                "80 NEXT F",
                "90 PRINT \"FANFARA KONCOWA!\"",
                "100 BEEP 523, 120",
                "110 BEEP 659, 120",
                "120 BEEP 784, 260",
                "130 PRINT \"WYKONANO POMYSLNIE!\"",
                "140 END"
            },
            // Sample 1: Zgadnij Liczbe
            new string[]
            {
                "10 REM --- ZGADNIJ LICZBE 1-50 ---",
                "20 CLS",
                "30 PRINT \"LOSOWANIE UKRYTEJ LICZBY...\"",
                "40 SECRET = 32",
                "50 TRIES = 0",
                "60 GUESS = 10",
                "70 TRIES = TRIES + 1",
                "80 PRINT \"PROBA \"; TRIES; \": STRZAL \"; GUESS",
                "90 IF GUESS = SECRET THEN GOTO 160",
                "100 IF GUESS < SECRET THEN PRINT \"-> ZA MALO!\"",
                "110 IF GUESS > SECRET THEN PRINT \"-> ZA DUZO!\"",
                "120 BEEP 320, 50",
                "130 GUESS = GUESS + 11",
                "140 IF GUESS > 50 THEN GUESS = GUESS - 45",
                "150 GOTO 70",
                "160 PRINT \"-> BRAWO! TRAFIONO W \"; TRIES; \" PROBIE!\"",
                "170 BEEP 880, 200",
                "180 END"
            },
            // Sample 2: Tabliczka Mnozenia
            new string[]
            {
                "10 REM --- TABLICZKA MNOZENIA ---",
                "20 CLS",
                "30 PRINT \"TABLICZKA MNOZENIA 1-5\"",
                "40 PRINT \"====================\"",
                "50 FOR I = 1 TO 5",
                "60 FOR J = 1 TO 5",
                "70 RES = I * J",
                "80 PRINT I; \" * \"; J; \" = \"; RES",
                "90 NEXT J",
                "100 PRINT \"--------------------\"",
                "110 BEEP 440 + I * 60, 40",
                "120 NEXT I",
                "130 PRINT \"TABLICA SKOMPLETOWANA!\"",
                "140 END"
            },
            // Sample 3: Generator Wzorow ASCII
            new string[]
            {
                "10 REM --- GENERATOR CHOINKI ASCII ---",
                "20 CLS",
                "30 PRINT \"WZOR GWIAZDKOWY:\"",
                "40 FOR R = 1 TO 8",
                "50 FOR C = 1 TO R",
                "60 PRINT \"*\";",
                "70 NEXT C",
                "80 PRINT \"\"",
                "90 BEEP 320 + R * 45, 30",
                "100 NEXT R",
                "110 PRINT \"GOTOWE!\"",
                "120 END"
            },
            // Sample 4: Ciag Fibonacciego
            new string[]
            {
                "10 REM --- CIAG FIBONACCIEGO ---",
                "20 CLS",
                "30 PRINT \"CIAG FIBONACCIEGO:\"",
                "40 A = 0",
                "50 B = 1",
                "60 PRINT A",
                "70 PRINT B",
                "80 FOR K = 1 TO 8",
                "90 C = A + B",
                "100 PRINT C",
                "110 BEEP 300 + C * 8, 40",
                "120 A = B",
                "130 B = C",
                "140 NEXT K",
                "150 PRINT \"KONIEC CIAGU.\"",
                "160 END"
            }
        };

        private static readonly string[] SampleNames = new string[]
        {
            "Dźwięki Chiptune",
            "Zgadnij Liczbę",
            "Tabliczka Mnożenia",
            "Wzory ASCII",
            "Ciąg Fibonacciego"
        };

        public BasicIdeApp(int x, int y, Action onClose) : base("Zonderq BASIC")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 620, 440, "Zonderq BASIC IDE - Środowisko Programistyczne");
            Window.CloseAction = Close;

            LoadSample(0);
            consoleLines.Add("Zonderq BASIC v1.0 [Cosmos Gen3]");
            consoleLines.Add("Wpisz program lub kliknij [URUCHOM].");
            consoleLines.Add("");
        }

        public override bool NeedsContinuousRedraw => isRunning || toneEndTimestamp > 0;

        public override void Close()
        {
            isRunning = false;
            ZonderqOS.Hardware.Speaker.Stop();
            sourceLines?.Clear();
            consoleLines?.Clear();
            base.Close();
            closeCallback?.Invoke();
        }

        private void LoadSample(int index)
        {
            sampleIndex = index % SamplePrograms.Length;
            sourceLines.Clear();
            string[] lines = SamplePrograms[sampleIndex];
            for (int i = 0; i < lines.Length; i++)
            {
                sourceLines.Add(lines[i]);
            }
            cursorRow = 0;
            cursorCol = 0;
            scrollRow = 0;
            statusMessage = "Wczytano: " + SampleNames[sampleIndex];
            statusColor = AccentColor;
        }

        public override void Update()
        {
            blinkCounter++;

            long now = Stopwatch.GetTimestamp();
            long freq = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;

            // PC Speaker tone shutoff
            if (toneEndTimestamp > 0 && now >= toneEndTimestamp)
            {
                ZonderqOS.Hardware.Speaker.Stop();
                toneEndTimestamp = 0;
            }

            // Check if interpreter is paused for SLEEP/BEEP delay
            if (isRunning && isPaused)
            {
                if (now >= resumeTimestamp)
                {
                    isPaused = false;
                }
                else
                {
                    return; // Wait until resume time
                }
            }

            // Step program execution (up to 30 statements per frame to maintain 60 FPS GUI)
            if (isRunning && !isPaused)
            {
                int steps = 0;
                while (isRunning && !isPaused && steps < 30)
                {
                    ExecuteStep();
                    steps++;
                }
            }
        }

        private void StartExecution()
        {
            StopExecution();

            // Parse and compile source lines into ordered program
            program.Clear();
            variables.Clear();
            callStack.Clear();
            forStack.Clear();
            pc = 0;

            for (int i = 0; i < sourceLines.Count; i++)
            {
                string line = sourceLines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                // Extract line number if present
                int spaceIdx = line.IndexOf(' ');
                int lineNum = 0;
                string stmt = line;

                if (spaceIdx > 0)
                {
                    string firstWord = line.Substring(0, spaceIdx);
                    if (int.TryParse(firstWord, out int num))
                    {
                        lineNum = num;
                        stmt = line.Substring(spaceIdx + 1).Trim();
                    }
                }

                if (lineNum == 0)
                {
                    lineNum = (i + 1) * 10;
                }

                ParsedLine pl;
                pl.LineNum = lineNum;
                pl.Statement = stmt;
                pl.SourceIndex = i;
                program.Add(pl);
            }

            // Bubble sort by line number
            for (int i = 0; i < program.Count - 1; i++)
            {
                for (int j = 0; j < program.Count - i - 1; j++)
                {
                    if (program[j].LineNum > program[j + 1].LineNum)
                    {
                        ParsedLine temp = program[j];
                        program[j] = program[j + 1];
                        program[j + 1] = temp;
                    }
                }
            }

            if (program.Count == 0)
            {
                statusMessage = "Brak instrukcji do wykonania";
                statusColor = YellowColor;
                return;
            }

            isRunning = true;
            statusMessage = "Wykonywanie...";
            statusColor = GreenColor;
        }

        private void StopExecution()
        {
            isRunning = false;
            isPaused = false;
            ZonderqOS.Hardware.Speaker.Stop();
            toneEndTimestamp = 0;
            statusMessage = "Zatrzymano";
            statusColor = MutedColor;
        }

        private void ExecuteStep()
        {
            if (pc < 0 || pc >= program.Count)
            {
                // Reached end of program
                isRunning = false;
                statusMessage = "Koniec programu (OK)";
                statusColor = GreenColor;
                PrintConsole("--- PROGRAM ZAKONCZONY (OK) ---");
                return;
            }

            ParsedLine current = program[pc];
            string stmt = current.Statement.Trim();
            pc++;

            if (string.IsNullOrEmpty(stmt) || stmt.StartsWith("REM", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // CLS
            if (stmt.Equals("CLS", StringComparison.OrdinalIgnoreCase))
            {
                consoleLines.Clear();
                consoleScrollRow = 0;
                return;
            }

            // END or STOP
            if (stmt.Equals("END", StringComparison.OrdinalIgnoreCase) || stmt.Equals("STOP", StringComparison.OrdinalIgnoreCase))
            {
                isRunning = false;
                statusMessage = "Program zakonczony (END)";
                statusColor = GreenColor;
                PrintConsole("--- KONIEC PROGRAMU ---");
                return;
            }

            // BEEP freq, duration
            if (stmt.StartsWith("BEEP ", StringComparison.OrdinalIgnoreCase))
            {
                string args = stmt.Substring(5).Trim();
                string[] parts = args.Split(',');
                int f = 440;
                int d = 100;
                if (parts.Length >= 1) f = EvaluateExpression(parts[0].Trim());
                if (parts.Length >= 2) d = EvaluateExpression(parts[1].Trim());

                f = Math.Max(40, Math.Min(5000, f));
                d = Math.Max(10, Math.Min(3000, d));

                ZonderqOS.Hardware.Speaker.Tone((ushort)f);
                long clock = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;
                toneEndTimestamp = Stopwatch.GetTimestamp() + (clock * d) / 1000;
                resumeTimestamp = toneEndTimestamp;
                isPaused = true;
                return;
            }

            // SLEEP ms
            if (stmt.StartsWith("SLEEP ", StringComparison.OrdinalIgnoreCase))
            {
                int ms = EvaluateExpression(stmt.Substring(6).Trim());
                ms = Math.Max(1, Math.Min(5000, ms));
                long clock = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;
                resumeTimestamp = Stopwatch.GetTimestamp() + (clock * ms) / 1000;
                isPaused = true;
                return;
            }

            // PRINT
            if (stmt.StartsWith("PRINT", StringComparison.OrdinalIgnoreCase))
            {
                string expr = stmt.Length > 5 ? stmt.Substring(5).Trim() : "";
                ExecutePrint(expr);
                return;
            }

            // GOTO lineNum
            if (stmt.StartsWith("GOTO ", StringComparison.OrdinalIgnoreCase))
            {
                int targetLine = EvaluateExpression(stmt.Substring(5).Trim());
                int targetIdx = FindProgramIndex(targetLine);
                if (targetIdx >= 0)
                {
                    pc = targetIdx;
                }
                else
                {
                    Error("Błąd GOTO: brak linii " + targetLine);
                }
                return;
            }

            // GOSUB lineNum
            if (stmt.StartsWith("GOSUB ", StringComparison.OrdinalIgnoreCase))
            {
                int targetLine = EvaluateExpression(stmt.Substring(6).Trim());
                int targetIdx = FindProgramIndex(targetLine);
                if (targetIdx >= 0)
                {
                    callStack.Push(pc);
                    pc = targetIdx;
                }
                else
                {
                    Error("Błąd GOSUB: brak linii " + targetLine);
                }
                return;
            }

            // RETURN
            if (stmt.Equals("RETURN", StringComparison.OrdinalIgnoreCase))
            {
                if (callStack.Count > 0)
                {
                    pc = callStack.Pop();
                }
                else
                {
                    Error("Błąd: RETURN bez GOSUB");
                }
                return;
            }

            // IF condition THEN statement/line
            if (stmt.StartsWith("IF ", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteIf(stmt.Substring(3).Trim());
                return;
            }

            // FOR var = start TO end [STEP s]
            if (stmt.StartsWith("FOR ", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteFor(stmt.Substring(4).Trim());
                return;
            }

            // NEXT [var]
            if (stmt.StartsWith("NEXT", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteNext(stmt.Length > 4 ? stmt.Substring(4).Trim() : "");
                return;
            }

            // LET var = expr or var = expr
            if (stmt.StartsWith("LET ", StringComparison.OrdinalIgnoreCase))
            {
                stmt = stmt.Substring(4).Trim();
            }

            int eqIdx = stmt.IndexOf('=');
            if (eqIdx > 0)
            {
                string varName = stmt.Substring(0, eqIdx).Trim().ToUpper();
                string expr = stmt.Substring(eqIdx + 1).Trim();
                int val = EvaluateExpression(expr);
                variables[varName] = val;
                return;
            }

            Error("Nierozpoznana instrukcja: " + stmt);
        }

        private void ExecutePrint(string expr)
        {
            if (string.IsNullOrEmpty(expr))
            {
                PrintConsole("");
                return;
            }

            bool suppressNewline = expr.EndsWith(";");
            if (suppressNewline)
            {
                expr = expr.Substring(0, expr.Length - 1).Trim();
            }

            string result = "";
            int i = 0;
            while (i < expr.Length)
            {
                while (i < expr.Length && (expr[i] == ' ' || expr[i] == ';' || expr[i] == ','))
                    i++;

                if (i >= expr.Length) break;

                if (expr[i] == '"')
                {
                    // String literal
                    int close = expr.IndexOf('"', i + 1);
                    if (close > i)
                    {
                        result += expr.Substring(i + 1, close - i - 1);
                        i = close + 1;
                    }
                    else
                    {
                        result += expr.Substring(i + 1);
                        i = expr.Length;
                    }
                }
                else
                {
                    // Expression / variable / number
                    int nextDelim = expr.IndexOfAny(new char[] { ';', ',' }, i);
                    string sub;
                    if (nextDelim >= 0)
                    {
                        sub = expr.Substring(i, nextDelim - i).Trim();
                        i = nextDelim + 1;
                    }
                    else
                    {
                        sub = expr.Substring(i).Trim();
                        i = expr.Length;
                    }

                    if (!string.IsNullOrEmpty(sub))
                    {
                        int val = EvaluateExpression(sub);
                        result += val.ToString();
                    }
                }
            }

            if (suppressNewline && consoleLines.Count > 0)
            {
                consoleLines[consoleLines.Count - 1] += result;
            }
            else
            {
                PrintConsole(result);
            }
        }

        private void ExecuteIf(string text)
        {
            int thenIdx = text.IndexOf(" THEN ", StringComparison.OrdinalIgnoreCase);
            if (thenIdx < 0)
            {
                Error("Błąd IF: brak THEN");
                return;
            }

            string condStr = text.Substring(0, thenIdx).Trim();
            string thenPart = text.Substring(thenIdx + 6).Trim();

            // Detect operator: <=, >=, <>, !=, ==, =, <, >
            string[] ops = new string[] { "<=", ">=", "<>", "!=", "==", "=", "<", ">" };
            string chosenOp = null;
            int opIdx = -1;

            for (int o = 0; o < ops.Length; o++)
            {
                int pos = condStr.IndexOf(ops[o]);
                if (pos > 0)
                {
                    chosenOp = ops[o];
                    opIdx = pos;
                    break;
                }
            }

            if (opIdx < 0)
            {
                Error("Błąd IF: brak operatora");
                return;
            }

            string leftStr = condStr.Substring(0, opIdx).Trim();
            string rightStr = condStr.Substring(opIdx + chosenOp.Length).Trim();
            int leftVal = EvaluateExpression(leftStr);
            int rightVal = EvaluateExpression(rightStr);

            bool conditionMet = false;
            if (chosenOp == "=" || chosenOp == "==") conditionMet = (leftVal == rightVal);
            else if (chosenOp == "<>" || chosenOp == "!=") conditionMet = (leftVal != rightVal);
            else if (chosenOp == "<") conditionMet = (leftVal < rightVal);
            else if (chosenOp == ">") conditionMet = (leftVal > rightVal);
            else if (chosenOp == "<=") conditionMet = (leftVal <= rightVal);
            else if (chosenOp == ">=") conditionMet = (leftVal >= rightVal);

            if (conditionMet)
            {
                if (int.TryParse(thenPart, out int targetLine))
                {
                    int idx = FindProgramIndex(targetLine);
                    if (idx >= 0) pc = idx;
                    else Error("Błąd IF: brak linii " + targetLine);
                }
                else
                {
                    // Execute statement inside THEN
                    ParsedLine temp;
                    temp.LineNum = 0;
                    temp.Statement = thenPart;
                    temp.SourceIndex = -1;

                    // Temporarily replace or jump
                    program.Insert(pc, temp);
                }
            }
        }

        private void ExecuteFor(string text)
        {
            // var = start TO end [STEP step]
            int eqIdx = text.IndexOf('=');
            int toIdx = text.IndexOf(" TO ", StringComparison.OrdinalIgnoreCase);
            if (eqIdx < 0 || toIdx < eqIdx)
            {
                Error("Błąd składni FOR");
                return;
            }

            string varName = text.Substring(0, eqIdx).Trim().ToUpper();
            string startStr = text.Substring(eqIdx + 1, toIdx - eqIdx - 1).Trim();
            string rest = text.Substring(toIdx + 4).Trim();

            int step = 1;
            int stepIdx = rest.IndexOf(" STEP ", StringComparison.OrdinalIgnoreCase);
            string endStr = rest;
            if (stepIdx >= 0)
            {
                endStr = rest.Substring(0, stepIdx).Trim();
                step = EvaluateExpression(rest.Substring(stepIdx + 6).Trim());
            }

            int startVal = EvaluateExpression(startStr);
            int endVal = EvaluateExpression(endStr);

            variables[varName] = startVal;

            ForState fs;
            fs.VarName = varName;
            fs.Target = endVal;
            fs.Step = step;
            fs.ProgramCounter = pc;
            forStack.Push(fs);
        }

        private void ExecuteNext(string varName)
        {
            if (forStack.Count == 0)
            {
                Error("Błąd: NEXT bez FOR");
                return;
            }

            ForState fs = forStack.Pop();
            int currentVal = variables.ContainsKey(fs.VarName) ? variables[fs.VarName] : 0;
            currentVal += fs.Step;
            variables[fs.VarName] = currentVal;

            bool loopAgain = (fs.Step >= 0 && currentVal <= fs.Target) || (fs.Step < 0 && currentVal >= fs.Target);
            if (loopAgain)
            {
                forStack.Push(fs);
                pc = fs.ProgramCounter;
            }
        }

        private int EvaluateExpression(string expr)
        {
            if (string.IsNullOrEmpty(expr))
                return 0;

            expr = expr.Trim();

            // Simple additive and multiplicative parser without allocation
            // Check for + or -
            int parenDepth = 0;
            for (int i = expr.Length - 1; i >= 0; i--)
            {
                char c = expr[i];
                if (c == ')') parenDepth++;
                else if (c == '(') parenDepth--;
                else if (parenDepth == 0 && (c == '+' || c == '-') && i > 0)
                {
                    string left = expr.Substring(0, i).Trim();
                    string right = expr.Substring(i + 1).Trim();
                    int l = EvaluateExpression(left);
                    int r = EvaluateExpression(right);
                    return c == '+' ? l + r : l - r;
                }
            }

            // Check for * or / or %
            for (int i = expr.Length - 1; i >= 0; i--)
            {
                char c = expr[i];
                if (c == ')') parenDepth++;
                else if (c == '(') parenDepth--;
                else if (parenDepth == 0 && (c == '*' || c == '/' || c == '%') && i > 0)
                {
                    string left = expr.Substring(0, i).Trim();
                    string right = expr.Substring(i + 1).Trim();
                    int l = EvaluateExpression(left);
                    int r = EvaluateExpression(right);
                    if (c == '*') return l * r;
                    if (c == '/') return r != 0 ? l / r : 0;
                    return r != 0 ? l % r : 0;
                }
            }

            // Parentheses stripped
            if (expr.StartsWith("(") && expr.EndsWith(")"))
            {
                return EvaluateExpression(expr.Substring(1, expr.Length - 2));
            }

            // Literal integer
            if (int.TryParse(expr, out int val))
            {
                return val;
            }

            // Variable lookup
            string upperVar = expr.ToUpper();
            if (variables.TryGetValue(upperVar, out int v))
            {
                return v;
            }

            return 0;
        }

        private int FindProgramIndex(int lineNum)
        {
            for (int i = 0; i < program.Count; i++)
            {
                if (program[i].LineNum == lineNum)
                    return i;
            }
            return -1;
        }

        private void PrintConsole(string line)
        {
            consoleLines.Add(line);
            if (consoleLines.Count > 200)
            {
                consoleLines.RemoveAt(0);
            }
            // Auto scroll to bottom
            consoleScrollRow = Math.Max(0, consoleLines.Count - 18);
        }

        private void Error(string msg)
        {
            isRunning = false;
            statusMessage = msg;
            statusColor = RedColor;
            PrintConsole("[BŁĄD] " + msg);
            ZonderqOS.Hardware.Speaker.Tone(220);
            long clock = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;
            toneEndTimestamp = Stopwatch.GetTimestamp() + (clock * 150) / 1000;
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key == null)
                return;

            if (key.Key == ConsoleKeyEx.Escape)
            {
                if (isRunning)
                {
                    StopExecution();
                    return;
                }
                Close();
                return;
            }

            if (key.Key == ConsoleKeyEx.F5)
            {
                StartExecution();
                return;
            }

            // Arrow navigation in editor
            if (key.Key == ConsoleKeyEx.UpArrow)
            {
                if (cursorRow > 0)
                {
                    cursorRow--;
                    cursorCol = Math.Min(cursorCol, sourceLines[cursorRow].Length);
                    EnsureCursorVisible();
                }
                return;
            }

            if (key.Key == ConsoleKeyEx.DownArrow)
            {
                if (cursorRow < sourceLines.Count - 1)
                {
                    cursorRow++;
                    cursorCol = Math.Min(cursorCol, sourceLines[cursorRow].Length);
                    EnsureCursorVisible();
                }
                return;
            }

            if (key.Key == ConsoleKeyEx.LeftArrow)
            {
                if (cursorCol > 0)
                {
                    cursorCol--;
                }
                else if (cursorRow > 0)
                {
                    cursorRow--;
                    cursorCol = sourceLines[cursorRow].Length;
                    EnsureCursorVisible();
                }
                return;
            }

            if (key.Key == ConsoleKeyEx.RightArrow)
            {
                if (cursorCol < sourceLines[cursorRow].Length)
                {
                    cursorCol++;
                }
                else if (cursorRow < sourceLines.Count - 1)
                {
                    cursorRow++;
                    cursorCol = 0;
                    EnsureCursorVisible();
                }
                return;
            }

            // Enter key (new line)
            if (key.Key == ConsoleKeyEx.Enter)
            {
                string line = sourceLines[cursorRow];
                string before = line.Substring(0, Math.Min(cursorCol, line.Length));
                string after = line.Substring(Math.Min(cursorCol, line.Length));

                sourceLines[cursorRow] = before;
                sourceLines.Insert(cursorRow + 1, after);
                cursorRow++;
                cursorCol = 0;
                EnsureCursorVisible();
                return;
            }

            // Backspace key
            if (key.Key == ConsoleKeyEx.Backspace)
            {
                string line = sourceLines[cursorRow];
                if (cursorCol > 0)
                {
                    sourceLines[cursorRow] = line.Substring(0, cursorCol - 1) + line.Substring(cursorCol);
                    cursorCol--;
                }
                else if (cursorRow > 0)
                {
                    string prevLine = sourceLines[cursorRow - 1];
                    cursorCol = prevLine.Length;
                    sourceLines[cursorRow - 1] = prevLine + line;
                    sourceLines.RemoveAt(cursorRow);
                    cursorRow--;
                    EnsureCursorVisible();
                }
                return;
            }

            // Printable characters
            if (key.KeyChar >= 32 && key.KeyChar < 127)
            {
                string line = sourceLines[cursorRow];
                cursorCol = Math.Min(cursorCol, line.Length);
                sourceLines[cursorRow] = line.Substring(0, cursorCol) + key.KeyChar + line.Substring(cursorCol);
                cursorCol++;
                EnsureCursorVisible();
            }
        }

        private void EnsureCursorVisible()
        {
            if (cursorRow < scrollRow)
                scrollRow = cursorRow;
            else if (cursorRow >= scrollRow + 18)
                scrollRow = cursorRow - 17;
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);

            if (!Window.Visible || Window.IsMinimized)
                return;

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;
            int tbY = contentY + 4;
            int tbH = 28;

            if (leftClicked && !leftWasClicked)
            {
                // [▶ URUCHOM]
                if (Hit(mouseX, mouseY, contentX + 6, tbY, 90, tbH))
                {
                    StartExecution();
                    return;
                }

                // [■ STOP]
                if (Hit(mouseX, mouseY, contentX + 102, tbY, 65, tbH))
                {
                    StopExecution();
                    return;
                }

                // [✦ PRZYKŁAD]
                if (Hit(mouseX, mouseY, contentX + 173, tbY, 150, tbH))
                {
                    StopExecution();
                    LoadSample(sampleIndex + 1);
                    return;
                }

                // [↺ WYCZYŚĆ]
                if (Hit(mouseX, mouseY, contentX + 329, tbY, 75, tbH))
                {
                    consoleLines.Clear();
                    consoleScrollRow = 0;
                    return;
                }

                // Click in Editor to set cursor
                int edX = contentX + 6;
                int edY = contentY + 38;
                int edW = 310;
                int edH = Window.Height - 88;

                if (Hit(mouseX, mouseY, edX, edY, edW, edH))
                {
                    int row = scrollRow + (mouseY - edY - 6) / 14;
                    if (row >= 0 && row < sourceLines.Count)
                    {
                        cursorRow = row;
                        int col = (mouseX - edX - 35) / 8;
                        cursorCol = Math.Max(0, Math.Min(col, sourceLines[cursorRow].Length));
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

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;
            int contentWidth = Math.Max(1, Window.Width - 16);
            int contentHeight = Math.Max(1, Window.Height - 46);

            // App background
            canvas.DrawFilledRectangle(Surface, contentX, contentY, contentWidth, contentHeight);

            // Toolbar (Top)
            int tbY = contentY + 4;
            int tbH = 28;
            canvas.DrawFilledRectangle(ToolbarBg, contentX + 4, tbY, contentWidth - 8, tbH);
            canvas.DrawRectangle(Border, contentX + 4, tbY, contentWidth - 8, tbH);

            DrawBtn(canvas, isRunning ? "DZIALA..." : "URUCHOM", contentX + 6, tbY + 2, 90, tbH - 4, isRunning ? YellowColor : GreenColor);
            DrawBtn(canvas, "STOP", contentX + 102, tbY + 2, 65, tbH - 4, RedColor);
            DrawBtn(canvas, "PRZYKLAD", contentX + 173, tbY + 2, 150, tbH - 4, AccentColor);
            DrawBtn(canvas, "WYCZYSC", contentX + 329, tbY + 2, 75, tbH - 4, TextColor);

            // Status message in toolbar
            SmallTextRenderer.Draw(canvas, statusMessage, contentX + 414, tbY + 10, statusColor);

            // Layout split: Left Editor (w: 320), Right Console (w: remainder)
            int bodyY = contentY + 36;
            int bodyH = contentHeight - 40;
            int editorW = 310;
            int consoleX = contentX + editorW + 12;
            int consoleW = contentWidth - editorW - 16;

            // Render Editor Panel
            RenderEditor(canvas, contentX + 6, bodyY, editorW, bodyH);

            // Render Console Panel
            RenderConsole(canvas, consoleX, bodyY, consoleW, bodyH);
        }

        private void RenderEditor(Canvas canvas, int x, int y, int w, int h)
        {
            canvas.DrawFilledRectangle(EditorBg, x, y, w, h);
            canvas.DrawRectangle(Border, x, y, w, h);

            // Header tab
            canvas.DrawFilledRectangle(ToolbarBg, x, y, w, 20);
            canvas.DrawLine(Border, x, y + 20, x + w, y + 20);
            SmallTextRenderer.Draw(canvas, "KOD BASIC (F5 - RUN)", x + 8, y + 6, TextColor);

            int viewY = y + 24;
            int maxRows = (h - 28) / 14;

            for (int r = 0; r < maxRows; r++)
            {
                int lineIdx = scrollRow + r;
                if (lineIdx >= sourceLines.Count)
                    break;

                int rowY = viewY + r * 14;

                // Active line highlight
                if (lineIdx == cursorRow)
                {
                    canvas.DrawFilledRectangle(ActiveLineBg, x + 2, rowY - 1, w - 4, 14);
                }

                // Line number
                string numStr = (lineIdx + 1).ToString();
                SmallTextRenderer.Draw(canvas, numStr, x + 6, rowY + 2, LineNumColor);

                // Code text
                string lineText = sourceLines[lineIdx];
                SmallTextRenderer.Draw(canvas, lineText, x + 35, rowY + 2, TextColor);

                // Cursor caret
                if (lineIdx == cursorRow && (blinkCounter % 20 < 12))
                {
                    int caretX = x + 35 + cursorCol * 8;
                    canvas.DrawFilledRectangle(SystemTheme.Accent, caretX, rowY + 1, 2, 11);
                }
            }
        }

        private void RenderConsole(Canvas canvas, int x, int y, int w, int h)
        {
            canvas.DrawFilledRectangle(ConsoleBg, x, y, w, h);
            canvas.DrawRectangle(Border, x, y, w, h);

            // Header tab
            canvas.DrawFilledRectangle(ToolbarBg, x, y, w, 20);
            canvas.DrawLine(Border, x, y + 20, x + w, y + 20);
            SmallTextRenderer.Draw(canvas, "KONSOLA WYJSCIOWA", x + 8, y + 6, TerminalGreen);

            int viewY = y + 24;
            int maxRows = (h - 28) / 14;

            for (int r = 0; r < maxRows; r++)
            {
                int lineIdx = consoleScrollRow + r;
                if (lineIdx >= consoleLines.Count)
                    break;

                int rowY = viewY + r * 14;
                string text = consoleLines[lineIdx];
                Color c = text.StartsWith("[BŁĄD]") ? RedColor : (text.StartsWith("---") ? AccentColor : TerminalGreen);
                SmallTextRenderer.Draw(canvas, text, x + 8, rowY + 2, c);
            }
        }

        private static void DrawBtn(Canvas canvas, string text, int x, int y, int w, int h, Color color)
        {
            canvas.DrawFilledRectangle(ToolbarBg, x, y, w, h);
            canvas.DrawRectangle(Border, x, y, w, h);
            SmallTextRenderer.DrawCentered(canvas, text, x, y + (h - 7) / 2, w, color);
        }
    }
}
