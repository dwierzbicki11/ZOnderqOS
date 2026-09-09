namespace ZonderqOS.Commands
{
    public class CmdEnv : ICommand
    {
        public string Name => "env";
        public string Description => "Print all environment variables";

        public void Execute(string[] args, ref string currentPath)
        {
            // Możemy pobrać słownik z EnvironmentManager lub wypisać kluczowe
            CommandIO.WriteLine($"USER={EnvironmentManager.Get("USER")}");
            CommandIO.WriteLine($"HOME={EnvironmentManager.Get("HOME")}");
            CommandIO.WriteLine($"HOSTNAME={EnvironmentManager.Get("HOSTNAME")}");
            CommandIO.WriteLine($"PATH={EnvironmentManager.Get("PATH")}");
            CommandIO.LastCommandSuccess = true;
        }
    }
}