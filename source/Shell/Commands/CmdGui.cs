using System;
using ZonderqOS.GUI;

namespace ZonderqOS.Commands
{
    public class CmdGui : ICommand
    {
        public string Name => "gui";
        public string Description => "Starts the interactive desktop environment with mouse support";

        public void Execute(string[] args, ref string currentPath)
        {
            try
            {
                CommandIO.WriteLine("[GUI] Starting window manager...");
                GuiManager manager = new GuiManager();
                manager.Run();
                CommandIO.WriteLine("[GUI] Returned to text shell.");
                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Error uruchamiania GUI: {ex.Message}", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}