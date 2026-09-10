using System;
using Cosmos.Kernel.System.Keyboard;
using ZonderqOS.SystemCore;

namespace ZonderqOS.GUI.Apps
{
    public class TerminalApp : Application
    {
        private readonly Action closeCallback;
        private readonly TerminalBox terminalBox;
        private string currentPath;
        private Action<string> nanoLauncher;

        public TerminalApp(int x, int y, Action onClose) : base("Terminal CLI")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 900, 560, "ZOnderqOS Terminal");
            Window.CloseAction = Close;

            currentPath = global::ZonderqOS.UserProfileManager.CurrentHome;
            if (string.IsNullOrEmpty(currentPath))
                currentPath = "/";

            terminalBox = new TerminalBox(8, 42, 884, 508);
            terminalBox.IsFocused = true;
            UpdatePrompt();

            terminalBox.PrintLine("ZOnderqOS Terminal - Gen3");
            terminalBox.PrintLine("Type 'help' to list available commands.");
            terminalBox.PrintLine("");

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
            int contentWidth = Math.Max(260, Window.Width - 16);
            int contentHeight = Math.Max(160, Window.Height - 50);

            terminalBox.X = Window.X + 8;
            terminalBox.Y = Window.Y + 42;
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
            string host = EnvironmentManager.Get("HOSTNAME") ?? "ZOnderqOS";
            terminalBox.Prompt = user + "@" + host + ":" + currentPath + "$ ";
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
            terminalBox.PrintLine(terminalBox.Prompt + MaskSensitiveCommand(command));
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
                terminalBox.PrintLine("Kernel error: " + ex.Message);
            }
            finally
            {
                string output = CommandIO.EndRedirection();
                PrintCommandOutput(output);
                UpdatePrompt();
            }
        }

        private static string MaskSensitiveCommand(string command)
        {
            if (string.IsNullOrEmpty(command))
                return string.Empty;

            // su currently accepts the password as its second argument. Do not persist that
            // credential in the terminal scrollback after Enter is pressed.
            if (command.StartsWith("su ", StringComparison.OrdinalIgnoreCase))
            {
                int usernameStart = 3;
                while (usernameStart < command.Length && command[usernameStart] == ' ')
                    usernameStart++;

                int passwordSeparator = command.IndexOf(' ', usernameStart);
                if (passwordSeparator > usernameStart)
                    return command.Substring(0, passwordSeparator) + " ********";
            }

            return command;
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
