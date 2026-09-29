using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdLs : ICommand
    {
        public string Name => "ls";
        public string Description => "List directory tree structure";

        public void Execute(string[] args, ref string currentPath)
        {
            string path = args.Length > 1
                ? PathResolver.GetAbsolutePath(currentPath, args[1])
                : currentPath;

            if (ProcFs.IsProcPath(path))
            {
                if (!ProcFs.TryList(path, out string[] entries))
                {
                    WriteMessage.WriteError($"procfs directory does not exist: {path}", "CMD");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                for (int i = 0; i < entries.Length; i++)
                    CommandIO.WriteLine(entries[i]);

                CommandIO.LastCommandSuccess = true;
                return;
            }

            Disk.Tree(path);
            CommandIO.LastCommandSuccess = true;
        }
    }
}
