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
        private int failedLoginAttempts;

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

                // All boot services initialize with the privileged boot context. From
                // this point onward no interactive shell or desktop is exposed until
                // credentials have been verified against /etc/shadow.
                UserManager.PrepareLogin();
                WriteMessage.WriteOK("ZonderqOS kernel successfully booted.", "SYS");
                Console.WriteLine();
                Console.WriteLine("Starting ZOnderqOS secure graphical login...");
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Boot critical error: {ex.Message}", "SYS");
            }
        }

        protected override void Run()
        {
            try
            {
                if (!SecurityContext.IsAuthenticated)
                {
                    sessionUser = null;

                    // Primary boot experience: graphical authentication followed by the
                    // desktop. If graphics/login initialization fails, retain the console
                    // prompt as a recovery path instead of locking the installation out.
                    bool graphicalLogin = LoginScreenManager.Run();
                    if (!graphicalLogin || !SecurityContext.IsAuthenticated)
                    {
                        RunLoginPrompt();
                        return;
                    }

                    failedLoginAttempts = 0;
                    history.Clear();
                    SynchronizeSession();

                    GuiManager manager = new GuiManager();
                    manager.Run();
                    return;
                }

                SynchronizeSession();

                string user = EnvironmentManager.Get("USER");
                if (string.IsNullOrEmpty(user)) user = SecurityContext.CurrentUser;

                string host = EnvironmentManager.Get("HOSTNAME");
                if (string.IsNullOrEmpty(host)) host = "ZonderqOS";

                Console.Write($"{user}@{host}:{path}$ ");
                string command = ReadLineWithHistory();

                if (!string.IsNullOrWhiteSpace(command))
                {
                    Command.Run(command, ref path);

                    // Do not retain history across logout or user switches. Besides being
                    // cleaner, this prevents a newly authenticated user from seeing commands
                    // entered by the previous session.
                    if (SecurityContext.IsAuthenticated &&
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
                WriteMessage.WriteError($"Wystąpił błąd jądra: {ex.Message}", "Kernel");
            }
        }

        private void RunLoginPrompt()
        {
            Console.Write("login: ");
            string username = Console.ReadLine();
            if (username != null)
                username = username.Trim();

            Console.Write("password: ");
            string password = ReadPassword();

            if (UserManager.TryStartSession(username, password))
            {
                failedLoginAttempts = 0;
                history.Clear();
                sessionUser = SecurityContext.CurrentUser;
                path = SecurityContext.CurrentHome;
                if (string.IsNullOrEmpty(path))
                    path = "/";

                Console.WriteLine($"Welcome, {SecurityContext.CurrentUser}.");
                Console.WriteLine();
                return;
            }

            failedLoginAttempts++;
            Console.WriteLine("Authentication failed.");

            // Small escalating delay slows trivial brute-force attempts without creating
            // a permanent lockout that could make a hobby OS installation unrecoverable.
            int delayMs = failedLoginAttempts >= 3 ? 2500 : 900;
            Thread.Sleep(delayMs);
            if (failedLoginAttempts >= 3)
                failedLoginAttempts = 0;
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

                if (windowWidth <= 0) windowWidth = 80;
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
