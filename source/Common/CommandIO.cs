using System;
using System.Collections.Generic;
using System.IO;

namespace ZonderqOS
{
    public static class CommandIO
    {
        private static List<string> _outBuffer = new List<string>();
        private static bool _isOutRedirected = false;
        private static Stack<TextWriter> _consoleWriters = new Stack<TextWriter>();
        private static Stack<StringWriter> _captureWriters = new Stack<StringWriter>();

        private static string _inBuffer = null;
        public static bool LastCommandSuccess { get; set; } = true;

        // GUI applications can provide native interactive handlers for commands
        // that historically owned the Console (for example nano).
        public static Action<string> NanoLauncher { get; set; }

        public static void SetInput(string input)
        {
            _inBuffer = input;
        }

        public static string GetInput()
        {
            return _inBuffer;
        }

        public static bool HasInput => !string.IsNullOrEmpty(_inBuffer);

        public static void StartRedirection()
        {
            _outBuffer.Clear();
            _isOutRedirected = true;

            TextWriter previous = Console.Out;
            StringWriter capture = new StringWriter();
            _consoleWriters.Push(previous);
            _captureWriters.Push(capture);
            Console.SetOut(capture);
        }

        public static string EndRedirection()
        {
            string consoleOutput = "";
            if (_captureWriters.Count > 0)
                consoleOutput = _captureWriters.Pop().ToString();

            if (_consoleWriters.Count > 0)
                Console.SetOut(_consoleWriters.Pop());

            _isOutRedirected = false;

            string commandOutput = string.Join("\n", _outBuffer);
            if (_outBuffer.Count > 0)
                commandOutput += "\n";

            _outBuffer.Clear();
            return commandOutput + consoleOutput;
        }

        public static void WriteLine(string text)
        {
            if (_isOutRedirected)
                _outBuffer.Add(text ?? "");
            else
                Console.WriteLine(text);
        }

        public static void Write(string text)
        {
            if (_isOutRedirected)
            {
                if (_outBuffer.Count == 0)
                    _outBuffer.Add(text ?? "");
                else
                    _outBuffer[_outBuffer.Count - 1] += text ?? "";
            }
            else
                Console.Write(text);
        }
    }
}
