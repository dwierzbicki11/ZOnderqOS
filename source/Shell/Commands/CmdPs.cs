using System;
using ZonderqOS.SystemCore;

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
                var processes = ProcessManager.GetActiveProcesses();

                CommandIO.WriteLine("PID    STATE      NAME");
                CommandIO.WriteLine("----------------------------------------");

                foreach (var p in processes)
                {
                    string state = p.IsRunning ? "RUNNING" : "ZOMBIE";
                    CommandIO.WriteLine($"{p.PID,-6} {state,-10} {p.Name}");
                }
                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                CommandIO.WriteLine($"[ERROR] Process manager failure: {ex.Message}");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}