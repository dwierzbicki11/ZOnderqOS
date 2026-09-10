using System;
using System.Collections.Generic;
using System.Threading;
using Cosmos.Kernel.System.Network;
using ZonderqOS.GUI;
using ZonderqOS.SystemCore;
using Sys = Cosmos.Kernel.System;

namespace ZonderqOS
{
    public class Kernel : Sys.Kernel
    {
        private const int MaxHistoryEntries = 100;
        private string path = "/root";
        private readonly List<string> history = new List<string>();
        private string sessionUser;

        protected override void BeforeRun()
        {
            try
            {
                Console.Clear();
                Disk.Initialize();
                UserManager.Initialize();
                Command.Initialize();
                EnvironmentManager.Initialize();
                SecurityLogger.Initialize();
                PermissionManager.Initialize();
                Network.Initialize();
                SystemGuardian.Initialize();

                UserManager.PrepareLogin();
                WriteMessage.WriteOK("ZonderqOS kernel successfully booted.", "SYS");
                Console.WriteLine();
                Console.WriteLine("Starting ZOnderqOS secure graphical login...");
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError("Boot critical error: " + ex.Message, "SYS");
            }
        }

        protected override void Run()
        {
            try
            {
                if (!SecurityContext.IsAuthenticated)
                {
                    sessionUser = null;

                    bool graphicalLogin = LoginScreenManager.Run();
                    if (!graphicalLogin || !SecurityContext.IsAuthenticated)
                    {
                        RunLoginPrompt();
                        return;
                    }

                    history.Clear();
                    SynchronizeSession();

                    GuiManager manager = new GuiManager();
                    manager.Run();
                    return;
                }

                SynchronizeSession();

                string user = EnvironmentManager.Get("USER");
                if (string.IsNullOrEmpty(user))
                    user = SecurityContext.CurrentUser;

                string host = EnvironmentManager.Get("HOSTNAME");
                if (string.IsNullOrEmpty(host))
                    host = "ZonderqOS";

                Console.Write(user + "@" + host + ":" + path + "$ ");
                string command = ReadLineWithHistory();

                if (!string.IsNullOrWhiteSpace(command))
                {
                    Command.Run(command, ref path);

                    // Password-bearing account commands are deliberately excluded from
                    // history. History is also cleared whenever the authenticated user changes.
                    if (SecurityContext.IsAuthenticated && !IsSensitiveCommand(command) &&
                        (history.Count == 0 || history[history.Count - 1] != command))
                    {
                        history.Add(command);
                        while (history.Count > MaxHistoryEntries)
                            history.RemoveAt(0);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError("Wystąpił błąd jądra: " + ex.Message, "Kernel");
            }
        }

        private void RunLoginPrompt()
        {
            Console.Write("login: ");
            string username = Console.ReadLine();
            if (username != null)
                username = username.Trim();

            int retryAfter;
            if (!AuthenticationGuard.CanAttempt(username, out retryAfter))
            {
                Console.WriteLine("Authentication temporarily blocked. Retry in " + retryAfter + " s.");
                Thread.Sleep(System.Math.Min(2000, retryAfter * 250));
                return;
            }

            Console.Write("password: ");
            string password = ReadPassword();

            if (UserManager.TryStartSession(username, password))
            {
                AuthenticationGuard.RecordSuccess(username);
                password = null;
                history.Clear();
                sessionUser = SecurityContext.CurrentUser;
                path = SecurityContext.CurrentHome;
                if (string.IsNullOrEmpty(path))
                    path = "/";

                Console.WriteLine("Welcome, " + SecurityContext.CurrentUser + ".");
                Console.WriteLine();
                return;
            }

            password = null;
            AuthenticationGuard.RecordFailure(username);
            retryAfter = AuthenticationGuard.GetRetryAfterSeconds(username);
            Console.WriteLine(retryAfter > 0
                ? "Authentication failed. Temporary delay: " + retryAfter + " s."
                : "Authentication failed.");

            Thread.Sleep(retryAfter > 0 ? System.Math.Min(2500, retryAfter * 300) : 600);
        }

        private void SynchronizeSession()
        {
            string currentUser = SecurityContext.CurrentUser ?? string.Empty;
            if (sessionUser == currentUser)
                return;

            history.Clear();
            sessionUser = currentUser;
            path = SecurityContext.CurrentHome;
            if (string.IsNullOrEmpty(path))
                path = "/";
        }

        private static bool IsSensitiveCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            string trimmed = command.TrimStart();
            return StartsWithCommand(trimmed, "su") || StartsWithCommand(trimmed, "useradd");
        }

        private static bool StartsWithCommand(string input, string commandName)
        {
            if (!input.StartsWith(commandName, StringComparison.OrdinalIgnoreCase))
                return false;
            return input.Length == commandName.Length ||
                   (input.Length > commandName.Length && char.IsWhiteSpace(input[commandName.Length]));
        }

        private string ReadPassword()
        {
            string password = string.Empty;
            while (true)
            {
                ConsoleKeyInfo keyInfo = Console.ReadKey(true);
                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return password;
                }

                if (keyInfo.Key == ConsoleKey.Backspace)
                {
                    if (password.Length > 0)
                    {
                        password = password.Substring(0, password.Length - 1);
                        Console.Write("\b \b");
                    }
                    continue;
                }

                if (keyInfo.KeyChar >= 32 && keyInfo.KeyChar <= 126 && password.Length < 128)
                {
                    password += keyInfo.KeyChar;
                    Console.Write('*');
                }
            }
        }

        private string ReadLineWithHistory()
        {
            string currentInput = "";
            int cursorPosition = 0;
            int historyIndex = history.Count;
            int startLeft = Console.CursorLeft;
            int startTop = Console.CursorTop;

            while (true)
            {
                var keyInfo = Console.ReadKey(true);

                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    break;
                }
                else if (keyInfo.Key == ConsoleKey.Backspace)
                {
                    if (cursorPosition > 0 && currentInput.Length > 0)
                    {
                        currentInput = currentInput.Remove(cursorPosition - 1, 1);
                        cursorPosition--;
                        RefreshLine(startLeft, startTop, currentInput, cursorPosition);
                    }
                }
                else if (keyInfo.Key == ConsoleKey.Delete)
                {
                    if (cursorPosition < currentInput.Length)
                    {
                        currentInput = currentInput.Remove(cursorPosition, 1);
                        RefreshLine(startLeft, startTop, currentInput, cursorPosition);
                    }
                }
                else if (keyInfo.Key == ConsoleKey.LeftArrow)
                {
                    if (cursorPosition > 0)
                    {
                        cursorPosition--;
                        SetConsoleCursor(startLeft, startTop, cursorPosition);
                    }
                }
                else if (keyInfo.Key == ConsoleKey.RightArrow)
                {
                    if (cursorPosition < currentInput.Length)
                    {
                        cursorPosition++;
                        SetConsoleCursor(startLeft, startTop, cursorPosition);
                    }
                }
                else if (keyInfo.Key == ConsoleKey.UpArrow)
                {
                    if (history.Count > 0 && historyIndex > 0)
                    {
                        historyIndex--;
                        currentInput = history[historyIndex];
                        cursorPosition = currentInput.Length;
                        RefreshLine(startLeft, startTop, currentInput, cursorPosition);
                    }
                }
                else if (keyInfo.Key == ConsoleKey.DownArrow)
                {
                    if (history.Count > 0 && historyIndex < history.Count - 1)
                    {
                        historyIndex++;
                        currentInput = history[historyIndex];
                        cursorPosition = currentInput.Length;
                        RefreshLine(startLeft, startTop, currentInput, cursorPosition);
                    }
                    else if (historyIndex >= history.Count - 1)
                    {
                        historyIndex = history.Count;
                        currentInput = "";
                        cursorPosition = 0;
                        RefreshLine(startLeft, startTop, currentInput, cursorPosition);
                    }
                }
                else if (keyInfo.KeyChar >= 32 && keyInfo.KeyChar <= 126)
                {
                    currentInput = currentInput.Insert(cursorPosition, keyInfo.KeyChar.ToString());
                    cursorPosition++;
                    RefreshLine(startLeft, startTop, currentInput, cursorPosition);
                }
            }

            return currentInput;
        }

        private void RefreshLine(int startLeft, int startTop, string currentInput, int cursorPosition)
        {
            try
            {
                Console.SetCursorPosition(startLeft, startTop);
                Console.Write(currentInput + " ");
                SetConsoleCursor(startLeft, startTop, cursorPosition);
            }
            catch
            {
            }
        }

        private void SetConsoleCursor(int startLeft, int startTop, int cursorPosition)
        {
            try
            {
                int targetLeft = startLeft + cursorPosition;
                int windowWidth = 80;
                try
                {
                    windowWidth = Console.WindowWidth;
                }
                catch
                {
                }

                if (windowWidth <= 0)
                    windowWidth = 80;
                int targetTop = startTop + (targetLeft / windowWidth);
                targetLeft %= windowWidth;
                Console.SetCursorPosition(targetLeft, targetTop);
            }
            catch
            {
            }
        }
    }
}
