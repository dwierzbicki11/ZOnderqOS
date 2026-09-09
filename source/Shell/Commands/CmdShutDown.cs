using ZonderqOS.Commands;

public class CmdShutDown : ICommand
{
    public string Name => "shutdown";

    public string Description => "Shutdown system";

    public void Execute(string[] args, ref string currentPath)
    {
        Cosmos.Kernel.System.Power.Shutdown();
    }
}