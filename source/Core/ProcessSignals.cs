namespace ZonderqOS.SystemCore
{
    public enum ProcessSignal
    {
        Check = 0,
        Interrupt = 2,
        Kill = 9,
        Terminate = 15
    }

    public enum ProcessSignalResult
    {
        Sent,
        Exists,
        NotFound,
        Unsupported
    }

    public static class ProcessSignalNames
    {
        public static string Name(ProcessSignal signal)
        {
            switch (signal)
            {
                case ProcessSignal.Check: return "CHECK";
                case ProcessSignal.Interrupt: return "INT";
                case ProcessSignal.Kill: return "KILL";
                case ProcessSignal.Terminate: return "TERM";
                default: return ((int)signal).ToString();
            }
        }

        public static bool TryParse(string value, out ProcessSignal signal)
        {
            signal = ProcessSignal.Terminate;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim().ToUpperInvariant();
            if (normalized.StartsWith("SIG"))
                normalized = normalized.Substring(3);

            if (normalized == "0" || normalized == "CHECK")
            {
                signal = ProcessSignal.Check;
                return true;
            }

            if (normalized == "2" || normalized == "INT")
            {
                signal = ProcessSignal.Interrupt;
                return true;
            }

            if (normalized == "9" || normalized == "KILL")
            {
                signal = ProcessSignal.Kill;
                return true;
            }

            if (normalized == "15" || normalized == "TERM")
            {
                signal = ProcessSignal.Terminate;
                return true;
            }

            return false;
        }
    }
}
