namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Small numeric helper used by GUI applications.
    /// It keeps mixed byte/int layout calculations explicit, avoiding overload
    /// ambiguity with properties such as Font.Width on Cosmos Gen3.
    /// </summary>
    internal static class Math
    {
        public static int Max(int a, int b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, byte b) => a > b ? a : b;
        public static int Min(int a, byte b) => a < b ? a : b;
        public static int Max(byte a, int b) => a > b ? a : b;
        public static int Min(byte a, int b) => a < b ? a : b;

        public static uint Max(uint a, uint b) => a > b ? a : b;
        public static uint Min(uint a, uint b) => a < b ? a : b;
        public static long Max(long a, long b) => a > b ? a : b;
        public static long Min(long a, long b) => a < b ? a : b;
        public static ulong Max(ulong a, ulong b) => a > b ? a : b;
        public static ulong Min(ulong a, ulong b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static double Max(double a, double b) => a > b ? a : b;
        public static double Min(double a, double b) => a < b ? a : b;
    }
}
