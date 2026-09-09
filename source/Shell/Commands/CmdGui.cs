using System;
using ZonderqOS.GUI;

namespace ZonderqOS.Commands
{
    public class CmdGui : ICommand
    {
        public string Name => "gui";
        public string Description => "Uruchamia interaktywne środowisko okienkowe z obsługą myszy";

        public void Execute(string[] args, ref string currentPath)
        {
            try
            {
                CommandIO.WriteLine("[GUI] Uruchamianie menedżera okien...");
                GuiManager manager = new GuiManager();
                manager.Run();
                CommandIO.WriteLine("[GUI] Powrót do powłoki tekstowej.");
                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Błąd uruchamiania GUI: {ex.Message}", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}