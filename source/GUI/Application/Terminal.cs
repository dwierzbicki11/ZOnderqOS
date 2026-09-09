using System;
using Cosmos.Kernel.System.Keyboard;
using ZonderqOS.GUI;
using ZonderqOS.SystemCore; // Dla EnvironmentManager / Command (podmień na właściwe przestrzenie nazw ze swojego kodu)

namespace ZonderqOS.GUI.Apps
{
    public class TerminalApp : Window
    {
        private TerminalBox terminalBox;
        private Action closeCallback;
        
        // Zmienna instancyjna utrzymująca ścieżkę tego konkretnego terminala
        private string currentPath = "/root"; 

        public TerminalApp(int x, int y, Action onClose) : base(x, y, 540, 380, "ZonderqOS Terminal")
        {
            closeCallback = onClose;

            terminalBox = new TerminalBox(10, 40, 520, 290);
            terminalBox.IsFocused = true;
            UpdatePrompt(); // Inicjalizacja pierwszej linijki (np. root@ZonderqOS)
            
            terminalBox.PrintLine("ZonderqOS GUI Terminal v0.3");
            terminalBox.PrintLine("Type 'help' to see available commands.");
            terminalBox.PrintLine("----------------------------------------");

            AddChild(terminalBox);

            var closeBtn = new Button(210, 340, 120, 30, "Zakończ sesję", () => {
                closeCallback?.Invoke();
            });
            AddChild(closeBtn);
        }

        // Aktualizacja prefixu linii poleceń
        private void UpdatePrompt()
        {
            string user = EnvironmentManager.Get("USER") ?? "root";
            string host = EnvironmentManager.Get("HOSTNAME") ?? "ZonderqOS";
            terminalBox.Prompt = $"{user}@{host}:{currentPath}$ ";
        }

        public void HandleKeyboard(KeyEvent key)
        {
            // Przekazanie wpisywania do kontrolki
            terminalBox.HandleKey(key);
            
            // Logika wywoływana po wciśnięciu ENTER
            if (key.Key == ConsoleKeyEx.Enter)
            {
                string command = terminalBox.Text.Trim();
                
                // Odbicie wpisanej komendy do logu terminala
                terminalBox.PrintLine(terminalBox.Prompt + command);
                terminalBox.ClearInput();

                if (!string.IsNullOrEmpty(command))
                {
                    try
                    {
                        // Wykonanie komendy sprzętowej
                        Command.Run(command, ref currentPath);
                        
                        // Zabezpieczenie: Odświeżamy prompt, bo komenda (np. 'cd') mogła zmienić ścieżkę
                        UpdatePrompt();
                        
                        // Tutaj w przyszłości: jeśli command.Run zrzuci output do zmiennej,
                        // dodaj go przez terminalBox.PrintLine(wynik);
                    }
                    catch (Exception ex)
                    {
                        terminalBox.PrintLine($"Błąd jądra: {ex.Message}");
                    }
                }
            }
        }
    }
}