using System;
using System.Collections.Generic;
using Cosmos.Kernel.System.Network;
using ZonderqOS.SystemCore;
using Sys = Cosmos.Kernel.System;

namespace ZonderqOS
{
    public class Kernel : Sys.Kernel
    {
        string path = "/root";
        List<string> history = new List<string>();

        protected override void BeforeRun()
        {
            try
            {
                Console.Clear();
                
                // 1. Inicjalizacja VFS i struktury FHS w root '/'
                Disk.Initialize();

                // 2. Inicjalizacja użytkowników i pliku /etc/passwd
                UserManager.Initialize();

                Command.Initialize();

                // 3. Inicjalizacja zmiennych środowiskowych i /etc/profile
                EnvironmentManager.Initialize();

                SecurityLogger.Initialize();

                PermissionManager.Initialize();

                Network.Initialize();

                SystemGuardian.Initialize();

                WriteMessage.WriteOK("ZonderqOS kernel successfully booted.", "SYS");
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
                // Dynamiczny uniksowy prompt: użytkownik@host:ścieżka$ 
                string user = EnvironmentManager.Get("USER");
                if (string.IsNullOrEmpty(user)) user = SecurityContext.CurrentUser;
                
                string host = EnvironmentManager.Get("HOSTNAME");
                if (string.IsNullOrEmpty(host)) host = "ZonderqOS";

                Console.Write($"{user}@{host}:{path}$ ");
                
                // Odczyt linii z obsługą historii, strzałek lewo/prawo i edycji w środku tekstu
                string command = ReadLineWithHistory();
                
                if (!string.IsNullOrWhiteSpace(command))
                {
                    Command.Run(command, ref path);
                    
                    // Zapisujemy w historii (ignorujemy duplikaty pod rząd)
                    if (history.Count == 0 || history[history.Count - 1] != command)
                    {
                        history.Add(command);
                    }
                }
            }
            catch(Exception ex)
            {
                WriteMessage.WriteError($"Wystąpił błąd jądra: {ex.Message}", "Kernel");
            }
        }

        /// <summary>
        /// Zaawansowany odczyt linii z obsługą kursora (lewo/prawo), historii (góra/dół) i edycji.
        /// </summary>
        private string ReadLineWithHistory()
        {
            string currentInput = "";
            int cursorPosition = 0;
            int historyIndex = history.Count;

            int startLeft = Console.CursorLeft;
            int startTop = Console.CursorTop;

            while (true)
            {
                var keyInfo = Console.ReadKey(true); // true = nie wypisuj znaku automatycznie

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
                // Wprowadzanie znaków w miejscu kursora
                else if (keyInfo.KeyChar >= 32 && keyInfo.KeyChar <= 126)
                {
                    currentInput = currentInput.Insert(cursorPosition, keyInfo.KeyChar.ToString());
                    cursorPosition++;
                    RefreshLine(startLeft, startTop, currentInput, cursorPosition);
                }
            }

            return currentInput;
        }

        /// <summary>
        /// Odświeża całą linię na ekranie i ustawia fizyczny kursor w odpowiednim miejscu.
        /// </summary>
        private void RefreshLine(int startLeft, int startTop, string currentInput, int cursorPosition)
        {
            try
            {
                Console.SetCursorPosition(startLeft, startTop);
                // Wypisz tekst + spację, aby wyczyścić ewentualne resztki po dłuższym napisie
                Console.Write(currentInput + " ");
                SetConsoleCursor(startLeft, startTop, cursorPosition);
            }
            catch
            {
                // Zabezpieczenie przed wyjściem poza ekran
            }
        }

        /// <summary>
        /// Ustawia fizyczny kursor konsoli na podstawie pozycji w tekście.
        /// </summary>
        private void SetConsoleCursor(int startLeft, int startTop, int cursorPosition)
        {
            try
            {
                int targetLeft = startLeft + cursorPosition;
                int windowWidth = 80;
                try { windowWidth = Console.WindowWidth; } catch { }

                int targetTop = startTop + (targetLeft / windowWidth);
                targetLeft = targetLeft % windowWidth;

                Console.SetCursorPosition(targetLeft, targetTop);
            }
            catch
            {
                // Zabezpieczenie przed błędem współrzędnych
            }
        }
    }
}