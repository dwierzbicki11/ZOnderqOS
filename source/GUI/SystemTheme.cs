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
                    case 1: return Color.FromArgb(130, 151, 170); // Steel Graphite
                    case 2: return Color.FromArgb(62, 168, 118);  // Emerald Forest
                    case 3: return Color.FromArgb(245, 55, 150);  // Dark Cyberpunk Neon
                    case 4: return Color.FromArgb(90, 205, 240);  // Nordic Frost
                    case 5: return Color.FromArgb(24, 75, 160);   // Retro 95 Classic Navy
                    case 6: return Color.FromArgb(240, 195, 45);  // OLED Midnight Gold
                    case 7: return Color.FromArgb(235, 65, 65);   // Crimson Flame
                    default: return Color.FromArgb(64, 143, 204); // Cosmos Blue
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
                    case 3: return Color.FromArgb(70, 24, 52);
                    case 4: return Color.FromArgb(32, 60, 75);
                    case 5: return Color.FromArgb(50, 65, 85);
                    case 6: return Color.FromArgb(65, 55, 20);
                    case 7: return Color.FromArgb(75, 28, 28);
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
                    case 3: return Color.FromArgb(255, 90, 180);
                    case 4: return Color.FromArgb(115, 220, 255);
                    case 5: return Color.FromArgb(70, 105, 150);
                    case 6: return Color.FromArgb(250, 215, 80);
                    case 7: return Color.FromArgb(250, 95, 95);
                    default: return Color.FromArgb(67, 135, 191);
                }
            }
        }
    }
}
