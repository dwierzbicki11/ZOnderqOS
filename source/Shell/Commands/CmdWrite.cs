namespace ZonderqOS.Commands
{
    public class CmdWrite : ICommand
    {
        public string Name => "write";
        public string Description => "Create or overwrite file with content";
        
        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 2)
            {
                string content = string.Join(" ", args, 2, args.Length - 2);
                // Zamiana literalnych \n na prawdziwe znaki nowej linii w pliku
                content = content.Replace("\\n", "\n");
                
                Disk.CreateFile(PathResolver.GetAbsolutePath(currentPath, args[1]), content);
                CommandIO.LastCommandSuccess = true;
            }
            else if (args.Length == 2)
            {
                Disk.CreateFile(PathResolver.GetAbsolutePath(currentPath, args[1]), string.Empty);
                CommandIO.LastCommandSuccess = true;
            }
            else 
            {
                WriteMessage.WriteError("Usage: write <file> [content]", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}