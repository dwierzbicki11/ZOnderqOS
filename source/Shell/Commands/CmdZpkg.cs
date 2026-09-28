using System.Collections.Generic;
using ZonderqOS.SystemCore.Packages;

namespace ZonderqOS.Commands
{
    public sealed class CmdZpkg : ICommand
    {
        private readonly List<InstalledPackage> packages = new List<InstalledPackage>(16);

        public string Name => "zpkg";
        public string Description => "Manage local ZonderqOS packages";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length < 2)
            {
                PrintHelp();
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string action = args[1].ToLowerInvariant();
            if (action == "list")
            {
                ListPackages();
                return;
            }

            if (action == "info")
            {
                if (args.Length < 3)
                {
                    WriteMessage.WriteError("Usage: zpkg info <name>", "PKG");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }
                ShowInfo(args[2]);
                return;
            }

            if (action == "verify")
            {
                if (args.Length < 3)
                {
                    WriteMessage.WriteError("Usage: zpkg verify <package-directory>", "PKG");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string path = PathResolver.GetAbsolutePath(currentPath, args[2]);
                PackageManifest manifest;
                int fileCount;
                ulong totalBytes;
                string error;
                if (!PackageManager.VerifySource(path, out manifest, out fileCount, out totalBytes, out error))
                {
                    WriteMessage.WriteError(error, "PKG");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                CommandIO.WriteLine("Package:     " + manifest.Name);
                CommandIO.WriteLine("Version:     " + manifest.Version);
                CommandIO.WriteLine("Files:       " + fileCount);
                CommandIO.WriteLine("Payload:     " + FormatBytes(totalBytes));
                CommandIO.WriteLine("Verification: OK");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (action == "install")
            {
                if (args.Length < 3)
                {
                    WriteMessage.WriteError("Usage: zpkg install <package-directory>", "PKG");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string path = PathResolver.GetAbsolutePath(currentPath, args[2]);
                InstalledPackage installed;
                string error;
                if (!PackageManager.Install(path, out installed, out error))
                {
                    WriteMessage.WriteError(error, "PKG");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                WriteMessage.WriteOK(
                    "Installed " + installed.Name + " " + installed.Version +
                    " -> " + installed.InstallPath,
                    "PKG");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (action == "remove")
            {
                if (args.Length < 3)
                {
                    WriteMessage.WriteError("Usage: zpkg remove <name>", "PKG");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string error;
                if (!PackageManager.Remove(args[2], out error))
                {
                    WriteMessage.WriteError(error, "PKG");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                WriteMessage.WriteOK("Removed package '" + args[2] + "'.", "PKG");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (action == "repair")
            {
                if (args.Length != 4)
                {
                    WriteMessage.WriteError("Usage: zpkg repair <name> <version>", "PKG");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string error;
                if (!PackageManager.RepairInterruptedInstall(args[2], args[3], out error))
                {
                    WriteMessage.WriteError(error, "PKG");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                WriteMessage.WriteOK("Cleared interrupted install for " + args[2] + " " + args[3] + ".", "PKG");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (action == "help" || action == "-h" || action == "--help")
            {
                PrintHelp();
                CommandIO.LastCommandSuccess = true;
                return;
            }

            WriteMessage.WriteError("Unknown zpkg action: " + action, "PKG");
            PrintHelp();
            CommandIO.LastCommandSuccess = false;
        }

        private void ListPackages()
        {
            string error;
            if (!PackageManager.ListInstalled(packages, out error))
            {
                WriteMessage.WriteError(error, "PKG");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (packages.Count == 0)
            {
                CommandIO.WriteLine("No local packages installed.");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            CommandIO.WriteLine("NAME                     VERSION");
            for (int i = 0; i < packages.Count; i++)
            {
                InstalledPackage package = packages[i];
                CommandIO.WriteLine(Pad(package.Name, 24) + " " + package.Version);
            }
            CommandIO.LastCommandSuccess = true;
        }

        private static void ShowInfo(string name)
        {
            InstalledPackage package;
            string error;
            if (!PackageManager.TryGetInstalled(name, out package, out error))
            {
                WriteMessage.WriteError(error, "PKG");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.WriteLine("Name:        " + package.Name);
            CommandIO.WriteLine("Version:     " + package.Version);
            CommandIO.WriteLine("Description: " +
                (string.IsNullOrEmpty(package.Description) ? "(none)" : package.Description));
            CommandIO.WriteLine("Store:       " + package.InstallPath);
            CommandIO.LastCommandSuccess = true;
        }

        private static void PrintHelp()
        {
            CommandIO.WriteLine("Usage:");
            CommandIO.WriteLine("  zpkg list");
            CommandIO.WriteLine("  zpkg info <name>");
            CommandIO.WriteLine("  zpkg verify <package-directory>");
            CommandIO.WriteLine("  zpkg install <package-directory>");
            CommandIO.WriteLine("  zpkg remove <name>");
            CommandIO.WriteLine("  zpkg repair <name> <version>");
            CommandIO.WriteLine("");
            CommandIO.WriteLine("Package directory format:");
            CommandIO.WriteLine("  package.zpkg");
            CommandIO.WriteLine("  payload/");
            CommandIO.WriteLine("");
            CommandIO.WriteLine("Stage 1 installs payload only into /opt/zpkg; it never executes scripts.");
        }

        private static string FormatBytes(ulong bytes)
        {
            const ulong KiB = 1024UL;
            const ulong MiB = 1024UL * KiB;
            if (bytes >= MiB)
                return (bytes / MiB) + " MiB";
            if (bytes >= KiB)
                return (bytes / KiB) + " KiB";
            return bytes + " B";
        }

        private static string Pad(string value, int width)
        {
            value = value ?? string.Empty;
            if (value.Length >= width)
                return value;
            return value + new string(' ', width - value.Length);
        }
    }
}
