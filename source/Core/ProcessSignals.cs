using System;

namespace ZonderqOS.SystemCore
{
    /// <summary>Supported cooperative process signals (not OS-level forced termination).</summary>
    public enum ProcessSignal
    {
        Interrupt = 2,
        Terminate = 15
    }

    public static class ProcessSignals
    {
        public static bool IsSupported(ProcessSignal signal) =>
            signal == ProcessSignal.Interrupt || signal == ProcessSignal.Terminate;

        public static string Name(ProcessSignal signal)
        {
            switch (signal)
            {
                case ProcessSignal.Interrupt: return "SIGINT";
                case ProcessSignal.Terminate: return "SIGTERM";
                default: return "UNKNOWN";
            }
        }

        public static bool TryParse(string value, out ProcessSignal signal)
        {
            signal = ProcessSignal.Terminate;
            if (string.IsNullOrEmpty(value))
                return false;

            string option = value.Trim();
            if (option.StartsWith("-", StringComparison.Ordinal))
                option = option.Substring(1);
            if (option.StartsWith("SIG", StringComparison.OrdinalIgnoreCase))
                option = option.Substring(3);

            if (option == "2" || option.Equals("INT", StringComparison.OrdinalIgnoreCase))
            {
                signal = ProcessSignal.Interrupt;
                return true;
            }

            if (option == "15" || option.Equals("TERM", StringComparison.OrdinalIgnoreCase))
            {
                signal = ProcessSignal.Terminate;
                return true;
            }

            return false;
        }
    }
}
