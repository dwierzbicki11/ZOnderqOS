using ZonderqOS.Commands;

class CmdReboot : ICommand
{
    public string Name => "reboot";

    public string Description => "Reboot your system";
    public void Execute(string[] args, ref string currentPath)
    {
        ZonderqOS.SystemCore.SystemPower.Reboot("shell-reboot");
    }
}