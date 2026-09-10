using System;
using System.Collections.Generic;
using System.IO;

namespace ZonderqOS.Commands
{
    public class CmdNano : ICommand
    {
        public string Name => "nano";
        public string Description => "Interactive text editor (nano <file>)";

        private const long MaxFileBytes = 1024 * 1024;
        private const int MaxDocumentCharacters = 1024 * 1024;

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length < 2)
            {
                WriteMessage.WriteError("Usage: nano <file>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string filePath = PathResolver.GetAbsolutePath(currentPath, args[1]);

            // When the GUI terminal is active, never call Console.ReadKey/SetCursorPosition.
            // Those APIs steal input from GuiManager and freeze the desktop. Open the
            // native GUI editor instead; CLI keeps the original editor below.
            if (CommandIO.NanoLauncher != null)
            {
                try
                {
                    CommandIO.NanoLauncher(filePath);
                    CommandIO.LastCommandSuccess = true;
                }
                catch (Exception ex)
                {
                    WriteMessage.WriteError($"Could not open GUI editor: {ex.Message}", "NANO");
                    CommandIO.LastCommandSuccess = false;
                }
                return;
            }

            List<string> lines = new List<string>();

            if (File.Exists(filePath))
            {
                if (!PermissionManager.CanRead(filePath, SecurityContext.CurrentUser))
                {
                    WriteMessage.WriteError($"Permission denied: Cannot read {filePath}", "SEC");
                    SecurityLogger.LogEvent("WARN", $"Unauthorized nano read attempt on {filePath} by {SecurityContext.CurrentUser}");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                try
                {
                    FileInfo fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length > MaxFileBytes)
                    {
                        WriteMessage.WriteError($"File is too large. Limit: {MaxFileBytes / 1024} KB.", "NANO");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }

                    string content = File.ReadAllText(filePath);
                    lines = new List<string>(content.Split(new[] { '\n' }));
                    for (int i = 0; i < lines.Count; i++)
                        lines[i] = lines[i].Replace("\r", "");
                }
                catch (Exception ex)
                {
                    WriteMessage.WriteError($"Could not read file: {ex.Message}", "FS");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }
            }

            if (lines.Count == 0)
                lines.Add("");

            RunEditor(filePath, lines);
            CommandIO.LastCommandSuccess = true;
        }

        private void RunEditor(string path, List<string> lines)
        {
            int cursorX = 0;
            int cursorY = 0;
            int scrollY = 0;
            int documentCharacters = CountDocumentCharacters(lines);
            bool running = true;
            string statusMessage = "";

            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Clear();

            while (running)
            {
                DrawScreen(path, lines, cursorX, cursorY, scrollY, statusMessage);
                statusMessage = "";
                var key = Console.ReadKey(true);
                bool control = (key.Modifiers & ConsoleModifiers.Control) != 0;

                if ((control && key.Key == ConsoleKey.O) || key.Key == ConsoleKey.F2)
                {
                    SaveFile(path, lines, out statusMessage);
                    continue;
                }

                if ((control && key.Key == ConsoleKey.X) || key.Key == ConsoleKey.F3 || key.Key == ConsoleKey.Escape)
                {
                    running = false;
                    continue;
                }

                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        if (cursorY > 0) cursorY--;
                        break;
                    case ConsoleKey.DownArrow:
                        if (cursorY < lines.Count - 1) cursorY++;
                        break;
                    case ConsoleKey.LeftArrow:
                        if (cursorX > 0)
                        {
                            cursorX--;
                        }
                        else if (cursorY > 0)
                        {
                            cursorY--;
                            cursorX = lines[cursorY].Length;
                        }
                        break;
                    case ConsoleKey.RightArrow:
                        if (cursorX < lines[cursorY].Length)
                        {
                            cursorX++;
                        }
                        else if (cursorY < lines.Count - 1)
                        {
                            cursorY++;
                            cursorX = 0;
                        }
                        break;
                    case ConsoleKey.Backspace:
                        if (cursorX > 0)
                        {
                            lines[cursorY] = lines[cursorY].Remove(cursorX - 1, 1);
                            cursorX--;
                            if (documentCharacters > 0) documentCharacters--;
                        }
                        else if (cursorY > 0)
                        {
                            int oldLen = lines[cursorY - 1].Length;
                            lines[cursorY - 1] += lines[cursorY];
                            lines.RemoveAt(cursorY);
                            cursorY--;
                            cursorX = oldLen;
                            if (documentCharacters > 0) documentCharacters--;
                        }
                        break;
                    case ConsoleKey.Enter:
                        if (documentCharacters >= MaxDocumentCharacters)
                        {
                            statusMessage = $"[Document limit: {MaxDocumentCharacters} characters]";
                            break;
                        }

                        string remainder = lines[cursorY].Substring(cursorX);
                        lines[cursorY] = lines[cursorY].Substring(0, cursorX);
                        lines.Insert(cursorY + 1, remainder);
                        cursorY++;
                        cursorX = 0;
                        documentCharacters++;
                        break;
                    default:
                        if (key.KeyChar >= 32 && key.KeyChar <= 126)
                        {
                            if (documentCharacters >= MaxDocumentCharacters)
                            {
                                statusMessage = $"[Document limit: {MaxDocumentCharacters} characters]";
                                break;
                            }

                            lines[cursorY] = lines[cursorY].Insert(cursorX, key.KeyChar.ToString());
                            cursorX++;
                            documentCharacters++;
                        }
                        break;
                }

                if (cursorX > lines[cursorY].Length)
                    cursorX = lines[cursorY].Length;

                const int screenHeight = 23;
                if (cursorY < scrollY) scrollY = cursorY;
                if (cursorY >= scrollY + screenHeight) scrollY = cursorY - screenHeight + 1;
            }

            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Clear();
        }

        private static int CountDocumentCharacters(List<string> lines)
        {
            long total = lines.Count > 0 ? lines.Count - 1 : 0;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (line != null)
                    total += line.Length;

                if (total >= MaxDocumentCharacters)
                    return MaxDocumentCharacters;
            }

            return (int)total;
        }

        private void DrawScreen(string path, List<string> lines, int cx, int cy, int scrollY, string status)
        {
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.White;
            Console.SetCursorPosition(0, 0);
            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.Write(("  ZonderqOS Nano Editor  -  " + path).PadRight(79));
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.White;

            const int displayLines = 23;
            for (int i = 0; i < displayLines; i++)
            {
                int lineIdx = scrollY + i;
                Console.SetCursorPosition(0, i + 1);
                if (lineIdx < lines.Count)
                {
                    string lineToPrint = lines[lineIdx];
                    if (lineToPrint.Length > 79) lineToPrint = lineToPrint.Substring(0, 79);
                    Console.Write(lineToPrint.PadRight(79));
                }
                else
                {
                    Console.Write(new string(' ', 79));
                }
            }

            Console.SetCursorPosition(0, 24);
            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.Black;
            string footer = string.IsNullOrEmpty(status) ? " ^O/F2 Save   ^X/F3 Exit" : " " + status;
            Console.Write(footer.PadRight(79));
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.White;
            int screenX = Math.Min(cx, 78);
            int screenY = (cy - scrollY) + 1;
            Console.SetCursorPosition(screenX, screenY);
        }

        private void SaveFile(string path, List<string> lines, out string message)
        {
            if (File.Exists(path) && !PermissionManager.CanWrite(path, SecurityContext.CurrentUser))
            {
                message = "[Permission denied]";
                SecurityLogger.LogEvent("WARN", $"Unauthorized nano write attempt on {path} by {SecurityContext.CurrentUser}");
                return;
            }

            try
            {
                Disk.CreateFile(path, string.Join("\n", lines));
                message = $"[Wrote {lines.Count} lines to {path}]";
                SecurityLogger.LogEvent("INFO", $"File edited via nano: {path}");
            }
            catch (Exception ex)
            {
                message = $"[Error saving: {ex.Message}]";
            }
        }
    }
}
