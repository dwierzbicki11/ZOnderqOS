using System;
using System.Diagnostics;
using System.Threading;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdHtop : ICommand
    {
        public string Name => "htop";
        public string Description => "Live CPU, memory and process monitor (htop [--once])";

        public void Execute(string[] args, ref string currentPath)
        {
            bool once = args.Length == 2 && args[1] == "--once";
            if (args.Length > 1 && !once)
            {
                CommandIO.WriteLine("Usage: htop [--once]");
                CommandIO.LastCommandSuccess = args.Length == 2 && (args[1] == "--help" || args[1] == "-h");
                return;
            }
            if (!once && !CommandIO.IsOutputRedirected && CommandIO.IsGraphicalCommand && CommandIO.HtopLauncher != null)
            {
                CommandIO.HtopLauncher();
                CommandIO.LastCommandSuccess = true;
                return;
            }
            HtopMonitor monitor = new HtopMonitor();
            monitor.Refresh();
            if (once || CommandIO.IsOutputRedirected || CommandIO.IsGraphicalCommand)
            {
                Thread.Sleep(250);
                monitor.Refresh();
                foreach (string line in monitor.Lines) CommandIO.WriteLine(line);
                CommandIO.LastCommandSuccess = true;
                return;
            }
            ConsoleColor foreground = Console.ForegroundColor;
            ConsoleColor background = Console.BackgroundColor;
            try
            {
                Console.BackgroundColor = ConsoleColor.Black;
                Console.ForegroundColor = ConsoleColor.Gray;
                long nextRefresh = 0;
                while (true)
                {
                    long now = Stopwatch.GetTimestamp();
                    if (now >= nextRefresh)
                    {
                        monitor.Refresh();
                        Draw(monitor);
                        nextRefresh = now + Stopwatch.Frequency;
                    }
                    if (Console.KeyAvailable)
                    {
                        ConsoleKey key = Console.ReadKey(true).Key;
                        if (key == ConsoleKey.Q || key == ConsoleKey.Escape) break;
                        if (key == ConsoleKey.C) monitor.SortByCpu = true;
                        if (key == ConsoleKey.P) monitor.SortByCpu = false;
                        if (key == ConsoleKey.UpArrow) monitor.Scroll = Math.Max(0, monitor.Scroll - 1);
                        if (key == ConsoleKey.DownArrow) monitor.Scroll = Math.Min(Math.Max(0, monitor.Lines.Count - 1), monitor.Scroll + 1);
                        nextRefresh = 0;
                    }
                    Thread.Sleep(25);
                }
                CommandIO.LastCommandSuccess = true;
            }
            finally
            {
                Console.ForegroundColor = foreground;
                Console.BackgroundColor = background;
                Console.Clear();
            }
        }

        private static void Draw(HtopMonitor monitor)
        {
            int width = Math.Max(1, Console.WindowWidth - 1);
            int height = Math.Max(1, Console.WindowHeight - 1);
            Console.SetCursorPosition(0, 0);
            for (int row = 0; row < height; row++)
            {
                int index = monitor.Scroll + row;
                string line = index < monitor.Lines.Count ? monitor.Lines[index] : "";
                if (line.Length > width) line = line.Substring(0, width);
                Console.WriteLine(line.PadRight(width));
            }
        }
    }
}
