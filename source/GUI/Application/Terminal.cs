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
        private Action<string> nanoLauncher;

        public TerminalApp(int x, int y, Action onClose) : base("Terminal CLI")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 800, 500, "ZonderqOS Terminal");
            Window.CloseAction = Close;
            terminalBox = new TerminalBox(10, 40, 780, 410);
            terminalBox.IsFocused = true;
            UpdatePrompt();
            terminalBox.PrintLine("ZonderqOS GUI Terminal v0.3");
            terminalBox.PrintLine("Type 'help' to see available commands.");
            terminalBox.PrintLine("----------------------------------------");
            Window.AddChild(terminalBox);
            UpdateLayout();
        }

        public void SetNanoLauncher(Action<string> launcher)
        {
            nanoLauncher = launcher;
            CommandIO.NanoLauncher = launcher;
        }

        private void UpdateLayout()
        {
            int contentWidth = Math.Max(200, Window.Width - 20);
            int contentHeight = Math.Max(120, Window.Height - 55);
            terminalBox.X = Window.X + 10;
            terminalBox.Y = Window.Y + 40;
            terminalBox.Width = contentWidth;
            terminalBox.Height = contentHeight;
            terminalBox.FontScale = Window.IsMaximized ? 1.0f : 0.8125f;
        }

        public override void Update()
        {
            UpdateLayout();
        }

        public override void HandleMouse(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            terminalBox.HandleMouse(mouseX, mouseY, isClicked, wasClicked);
            base.HandleMouse(mouseX, mouseY, isClicked, wasClicked);
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

            string normalized = output.Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = normalized.Split('\n');
            foreach (string line in lines)
            {
                if (line == "\u0001GUI_CLEAR\u0001")
                {
                    terminalBox.ClearOutput();
                    continue;
                }

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

            CommandIO.StartRedirection();
            try
            {
                Command.Run(command, ref currentPath);
            }
            catch (Exception ex)
            {
                terminalBox.PrintLine($"Błąd jądra: {ex.Message}");
            }
            finally
            {
                string output = CommandIO.EndRedirection();
                PrintCommandOutput(output);
                UpdatePrompt();
            }
        }

        public override void Close()
        {
            if (CommandIO.NanoLauncher == nanoLauncher)
                CommandIO.NanoLauncher = null;

            base.Close();
            closeCallback?.Invoke();
        }
    }
}