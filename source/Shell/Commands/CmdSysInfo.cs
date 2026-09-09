using System;

namespace ZonderqOS.Commands
{
    public class CmdSysInfo : ICommand
    {
        public string Name => "sysinfo";
        public string Description => "Display kernel & system info";
        public void Execute(string[] args, ref string currentPath)
        {
            CommandIO.WriteLine("=== ZonderqOS System Information ===");
            CommandIO.WriteLine("  OS Name:         ZonderqOS (Custom Cosmos Kernel)");
            CommandIO.WriteLine($"  Architecture:    {Cosmos.Kernel.System.Global.CurrentKernel}");
            CommandIO.WriteLine("  File System:     VFS over FAT32 / HAL Block Devices");
            CommandIO.WriteLine($"  Current Root:    {currentPath}");
            CommandIO.WriteLine("Status: STABLE");
            CommandIO.LastCommandSuccess = true;
        }
    }
}
