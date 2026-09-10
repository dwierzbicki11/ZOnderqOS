using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZonderqOS
{
    public static class CommandIO
    {
        private const int MaxCapturedCharacters = 256 * 1024;
        private const string TruncatedMarker = "\n[output truncated]\n";

        private static readonly Stack<CaptureBuffer> _outBuffers = new Stack<CaptureBuffer>();
        private static readonly Stack<TextWriter> _consoleWriters = new Stack<TextWriter>();
        private static readonly Stack<BoundedTextWriter> _captureWriters = new Stack<BoundedTextWriter>();

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
            _outBuffers.Push(new CaptureBuffer(MaxCapturedCharacters));

            TextWriter previous = Console.Out;
            BoundedTextWriter capture = new BoundedTextWriter(MaxCapturedCharacters);
            _consoleWriters.Push(previous);
            _captureWriters.Push(capture);
            Console.SetOut(capture);
        }

        public static string EndRedirection()
        {
            if (_outBuffers.Count == 0 || _captureWriters.Count == 0 || _consoleWriters.Count == 0)
                return string.Empty;

            BoundedTextWriter capture = _captureWriters.Pop();
            string consoleOutput = capture.GetText();
            capture.Dispose();

            Console.SetOut(_consoleWriters.Pop());

            CaptureBuffer commandCapture = _outBuffers.Pop();
            return commandCapture.GetText() + consoleOutput;
        }

        public static void WriteLine(string text)
        {
            if (_outBuffers.Count > 0)
                _outBuffers.Peek().AppendLine(text ?? string.Empty);
            else
                Console.WriteLine(text);
        }

        public static void Write(string text)
        {
            if (_outBuffers.Count > 0)
                _outBuffers.Peek().Append(text ?? string.Empty);
            else
                Console.Write(text);
        }

        private sealed class CaptureBuffer
        {
            private readonly int maxCharacters;
            private readonly StringBuilder builder = new StringBuilder();
            private bool truncated;

            public CaptureBuffer(int maxCharacters)
            {
                this.maxCharacters = maxCharacters;
            }

            public void Append(string value)
            {
                if (string.IsNullOrEmpty(value))
                    return;

                int remaining = maxCharacters - builder.Length;
                if (remaining <= 0)
                {
                    truncated = true;
                    return;
                }

                if (value.Length <= remaining)
                {
                    builder.Append(value);
                    return;
                }

                builder.Append(value, 0, remaining);
                truncated = true;
            }

            public void AppendLine(string value)
            {
                Append(value);
                Append("\n");
            }

            public string GetText()
            {
                string value = builder.ToString();
                return truncated ? value + TruncatedMarker : value;
            }
        }

        private sealed class BoundedTextWriter : TextWriter
        {
            private readonly CaptureBuffer capture;

            public BoundedTextWriter(int maxCharacters)
            {
                capture = new CaptureBuffer(maxCharacters);
            }

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value)
            {
                capture.Append(value.ToString());
            }

            public override void Write(string value)
            {
                capture.Append(value ?? string.Empty);
            }

            public override void WriteLine(string value)
            {
                capture.AppendLine(value ?? string.Empty);
            }

            public string GetText()
            {
                return capture.GetText();
            }
        }
    }
}
