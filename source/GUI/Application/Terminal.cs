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
            Window = new Window(x, y, 540, 380, "ZonderqOS Terminal");

            terminalBox = new TerminalBox(10, 40, 520, 290);
            terminalBox.IsFocused = true;
            UpdatePrompt();
            terminalBox.PrintLine("ZonderqOS GUI Terminal v0.3");
            terminalBox.PrintLine("Type 'help' to see available commands.");
            terminalBox.PrintLine("----------------------------------------");
            Window.AddChild(terminalBox);

            var closeBtn = new Button(210, 340, 120, 30, "Zakończ sesję", () =>
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
                Command.Run(command, ref currentPath);
                UpdatePrompt();
            }
            catch (Exception ex)
            {
                terminalBox.PrintLine($"Błąd jądra: {ex.Message}");
            }
        }

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
        }
    }
}