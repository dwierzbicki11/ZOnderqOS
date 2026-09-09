using System;
using Cosmos.Kernel.System.Keyboard;
using ZonderqOS.SystemCore;

namespace ZonderqOS.GUI.Apps
{
    public class TerminalApp : Application
    {
        private readonly Action closeCallback;
        private readonly TerminalBox terminalBox;
        private string currentPath = "/root";

        public TerminalApp(int x, int y, Action onClose) : base("Terminal CLI")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 800, 500, "ZonderqOS Terminal");

            terminalBox = new TerminalBox(10, 40, 780, 410);
            terminalBox.IsFocused = true;
            UpdatePrompt();
            terminalBox.PrintLine("ZonderqOS GUI Terminal v0.3");
            terminalBox.PrintLine("Type 'help' to see available commands.");
            terminalBox.PrintLine("----------------------------------------");
            Window.AddChild(terminalBox);

            var closeBtn = new Button(340, 460, 120, 30, "Zakończ sesję", () =>
            {
                Close();
            });
            Window.AddChild(closeBtn);
        }

        private void UpdatePrompt()
        {
            string user = EnvironmentManager.Get("USER") ?? "root";
            string host = EnvironmentManager.Get("HOSTNAME") ?? "ZonderqOS";
            terminalBox.Prompt = $"{user}@{host}:{currentPath}$ ";
        }

        private void PrintCommandOutput(string output)
        {
            if (string.IsNullOrEmpty(output))
                return;

            string[] lines = output.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            foreach (string line in lines)
            {
                if (line.Length > 0)
                    terminalBox.PrintLine(line);
            }
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            terminalBox.HandleKey(key);

            if (key.Key != ConsoleKeyEx.Enter)
                return;

            string command = terminalBox.Text.Trim();
            terminalBox.PrintLine(terminalBox.Prompt + command);
            terminalBox.ClearInput();

            if (string.IsNullOrEmpty(command))
                return;

            try
            {
                // Przechwytujemy cały output Command.Run zamiast wypisywać go do konsoli QEMU.
                CommandIO.StartRedirection();
                Command.Run(command, ref currentPath);
                string output = CommandIO.EndRedirection();
                PrintCommandOutput(output);
                UpdatePrompt();
            }
            catch (Exception ex)
            {
                // Nawet po błędzie kończymy przekierowanie, żeby kolejne komendy nadal działały.
                string output = CommandIO.EndRedirection();
                PrintCommandOutput(output);
                terminalBox.PrintLine($"Błąd jądra: {ex.Message}");
                UpdatePrompt();
            }
        }

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
        }
    }
}