using System;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdKill : ICommand
    {
        public string Name { get; } = "kill";
        public string Description { get; } = "Terminate a background process by PID: kill <pid>";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Użycie: kill <PID>");
                return;
            }

            if (int.TryParse(args[1], out int pid))
            {
                bool success = ProcessManager.Kill(pid);
                if (success)
                {
                    Console.WriteLine($"[OK] Wysłano sygnał zamknięcia do procesu PID {pid}.");
                }
                else
                {
                    Console.WriteLine($"[ERROR] Nie znaleziono aktywnego procesu o PID {pid}.");
                }
            }
            else
            {
                Console.WriteLine("[ERROR] Nieprawidłowy format PID.");
            }
        }
    }
}