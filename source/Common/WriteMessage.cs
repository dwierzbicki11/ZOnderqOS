using System;

namespace ZonderqOS
{
    public static class WriteMessage
    {
        // Central private method handling console I/O logic. Minimizes code duplication.
        private static void Write(string module, string status, string message, ConsoleColor statusColor)
        {
            // Draw module tag if provided
            if (!string.IsNullOrEmpty(module))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"[{module}] ");
            }

            // Draw operation status
            Console.ForegroundColor = statusColor;
            Console.Write($"[{status}] ");
            
            // Draw message in standard color
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(message);
        }

        public static void WriteError(string message, string module = "") 
            => Write(module, "ERROR", message, ConsoleColor.Red);

        public static void WriteInfo(string message, string module = "") 
            => Write(module, "INFO", message, ConsoleColor.Gray);

        public static void WriteOK(string message, string module = "") 
            => Write(module, "OK", message, ConsoleColor.Green);
    }
}