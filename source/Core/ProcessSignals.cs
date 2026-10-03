namespace ZonderqOS.SystemCore
{
    public enum ProcessSignal
    {
        Interrupt = 2,
        Terminate = 15
    }

    public static class ProcessSignals
    {
        public static bool IsSupported(ProcessSignal signal)
        {
            return signal == ProcessSignal.Interrupt ||
                   signal == ProcessSignal.Terminate;
        }

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

            string normalized = value.Trim();
            if (normalized.StartsWith("-", System.StringComparison.Ordinal))
                normalized = normalized.Substring(1);

            if (normalized.StartsWith("SIG", System.StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(3);

            if (normalized == "2" ||
                normalized.Equals("INT", System.StringComparison.OrdinalIgnoreCase))
            {
                signal = ProcessSignal.Interrupt;
                return true;
            }

            if (normalized == "15" ||
                normalized.Equals("TERM", System.StringComparison.OrdinalIgnoreCase))
            {
                signal = ProcessSignal.Terminate;
                return true;
            }

            return false;
        }
    }
}
