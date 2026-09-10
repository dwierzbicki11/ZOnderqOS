using System;
using System.Collections.Generic;
using System.IO;

namespace ZonderqOS
{
    public static class CommandIO
    {
        private static readonly Stack<List<string>> _outBuffers = new Stack<List<string>>();
        private static readonly Stack<TextWriter> _consoleWriters = new Stack<TextWriter>();
        private static readonly Stack<StringWriter> _captureWriters = new Stack<StringWriter>();

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
            _outBuffers.Push(new List<string>());

            TextWriter previous = Console.Out;
            StringWriter capture = new StringWriter();
            _consoleWriters.Push(previous);
            _captureWriters.Push(capture);
            Console.SetOut(capture);
        }

        public static string EndRedirection()
        {
            if (_outBuffers.Count == 0 || _captureWriters.Count == 0 || _consoleWriters.Count == 0)
                return string.Empty;

            StringWriter capture = _captureWriters.Pop();
            string consoleOutput = capture.ToString();
            capture.Dispose();

            Console.SetOut(_consoleWriters.Pop());

            List<string> buffer = _outBuffers.Pop();
            string commandOutput = string.Join("\n", buffer);
            if (buffer.Count > 0)
                commandOutput += "\n";

            return commandOutput + consoleOutput;
        }

        public static void WriteLine(string text)
        {
            if (_outBuffers.Count > 0)
                _outBuffers.Peek().Add(text ?? "");
            else
                Console.WriteLine(text);
        }

        public static void Write(string text)
        {
            if (_outBuffers.Count > 0)
            {
                List<string> buffer = _outBuffers.Peek();
                if (buffer.Count == 0)
                    buffer.Add(text ?? "");
                else
                    buffer[buffer.Count - 1] += text ?? "";
            }
            else
            {
                Console.Write(text);
            }
        }
    }
}
