using System.Drawing;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Central shell palette. Values are structs calculated on demand, so changing the
    /// accent does not require recreating windows or allocating new managed objects.
    /// </summary>
    public static class SystemTheme
    {
        public static Color Accent
        {
            get
            {
                switch (global::ZonderqOS.SystemSettings.AccentTheme)
                {
                    case 1: return Color.FromArgb(130, 151, 170);
                    case 2: return Color.FromArgb(62, 168, 118);
                    default: return Color.FromArgb(64, 143, 204);
                }
            }
        }

        public static Color AccentSoft
        {
            get
            {
                switch (global::ZonderqOS.SystemSettings.AccentTheme)
                {
                    case 1: return Color.FromArgb(48, 60, 70);
                    case 2: return Color.FromArgb(34, 68, 53);
                    default: return Color.FromArgb(43, 66, 88);
                }
            }
        }

        public static Color AccentBorder
        {
            get
            {
                switch (global::ZonderqOS.SystemSettings.AccentTheme)
                {
                    case 1: return Color.FromArgb(104, 123, 139);
                    case 2: return Color.FromArgb(57, 123, 92);
                    default: return Color.FromArgb(67, 135, 191);
                }
            }
        }
    }
}
