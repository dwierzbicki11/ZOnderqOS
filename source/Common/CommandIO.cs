using System;
using System.Collections.Generic;

namespace ZonderqOS
{
    public static class CommandIO
    {
        private static List<string> _outBuffer = new List<string>();
        private static bool _isOutRedirected = false;
        
        private static string _inBuffer = null;
        public static bool LastCommandSuccess { get; set; } = true;

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
        }

        public static string EndRedirection()
        {
            _isOutRedirected = false;
            string result = string.Join("\n", _outBuffer) + (_outBuffer.Count > 0 ? "\n" : "");
            _outBuffer.Clear();
            return result;
        }

        public static void WriteLine(string text)
        {
            if (_isOutRedirected)
            {
                _outBuffer.Add(text);
            }
            else
            {
                Console.WriteLine(text);
            }
        }

        public static void Write(string text)
        {
            if (_isOutRedirected)
            {
                if (_outBuffer.Count == 0) _outBuffer.Add(text);
                else _outBuffer[_outBuffer.Count - 1] += text;
            }
            else
            {
                Console.Write(text);
            }
        }
    }
}
