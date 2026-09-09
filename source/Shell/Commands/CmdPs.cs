using System;
using ZonderqOS.SystemCore; // Dopasuj przestrzeń nazw

namespace ZonderqOS.Commands
{
    public class CmdPs : ICommand
    {
        public string Name { get; } = "ps";
        public string Description { get; } = "Report a snapshot of current processes";

        public void Execute(string[] args, ref string currentPath)
        {
            try
            {
                // Odpytanie menedżera
                var processes = ProcessManager.GetActiveProcesses();

                Console.WriteLine("PID    STATE      NAME");
                Console.WriteLine("----------------------------------------");

                foreach (var p in processes)
                {
                    string state = p.IsRunning ? "RUNNING" : "ZOMBIE";
                    // Używamy formatowania interpolowanego do wyrównania kolumn (-6 oznacza 6 znaków w lewo)
                    Console.WriteLine($"{p.PID,-6} {state,-10} {p.Name}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Process manager failure: {ex.Message}");
            }
        }
    }
}