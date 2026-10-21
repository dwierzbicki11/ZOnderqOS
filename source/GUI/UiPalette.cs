using System.Drawing;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Opaque, allocation-free colors. Alpha blending is intentionally avoided:
    /// the Gen3 software canvas does not guarantee composited alpha.
    /// </summary>
    internal static class UiPalette
    {
        internal static readonly Color Surface = Color.FromArgb(25, 33, 43);
        internal static readonly Color SurfaceRaised = Color.FromArgb(32, 42, 54);
        internal static readonly Color SurfaceHover = Color.FromArgb(44, 61, 76);
        internal static readonly Color SurfacePressed = Color.FromArgb(35, 77, 106);
        internal static readonly Color Border = Color.FromArgb(64, 84, 100);
        internal static readonly Color Text = Color.FromArgb(236, 242, 248);
        internal static readonly Color TextMuted = Color.FromArgb(155, 171, 185);
        internal static readonly Color Danger = Color.FromArgb(176, 63, 76);
        internal static readonly Color DangerBorder = Color.FromArgb(228, 88, 102);
        internal static readonly Color ShadowOuter = Color.FromArgb(12, 17, 24);
        internal static readonly Color ShadowMiddle = Color.FromArgb(15, 20, 29);
        internal static readonly Color ShadowInner = Color.FromArgb(18, 24, 33);

        internal static Color Accent => SystemTheme.Accent;
        internal static Color AccentBorder => SystemTheme.AccentBorder;
        internal static Color AccentSoft => SystemTheme.AccentSoft;
    }
}
