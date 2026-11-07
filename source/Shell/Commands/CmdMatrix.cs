using System;

namespace ZonderqOS.Commands
{
    public sealed class CmdMatrix : ICommand
    {
        public string Name => "matrix";
        public string Description => "Matrix digital rain animation";

        public void Execute(string[] args, ref string currentPath)
        {
            CommandIO.WriteLine("=== Matrix Digital Rain ===");

            Random rng = new Random();
            int width = 45;
            int maxFrames = 35;
            if (args.Length > 1 && int.TryParse(args[1], out int f) && f > 0 && f <= 100)
            {
                maxFrames = f;
            }

            char[] chars = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F', 'X', 'Z', '%', '*', '+', '#' };

            for (int frame = 0; frame < maxFrames; frame++)
            {
                char[] line = new char[width];
                for (int x = 0; x < width; x++)
                {
                    if (rng.Next(0, 3) == 0)
                    {
                        line[x] = chars[rng.Next(0, chars.Length)];
                    }
                    else
                    {
                        line[x] = ' ';
                    }
                }

                CommandIO.WriteLine(new string(line));
            }

            CommandIO.WriteLine("[Matrix disconnect]");
            CommandIO.LastCommandSuccess = true;
        }
    }
}
