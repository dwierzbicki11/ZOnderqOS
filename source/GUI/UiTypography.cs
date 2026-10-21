using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Readable headings with a compact fallback when an application is fitted
    /// to a small 3.0.94 framebuffer. No new font files or raster allocations.
    /// </summary>
    internal static class UiTypography
    {
        internal static void Heading(Canvas canvas, string text, int x, int y, int maxWidth,
            Color color)
        {
            if (canvas == null || string.IsNullOrEmpty(text) || maxWidth <= 0)
                return;

            // Cosmos' bundled screen font is used when it fits.
            // Compact glyphs are clipped without building temporary strings.
            if (TextHelper.GetTextWidth(text, PCScreenFont.DefaultFont) <= maxWidth)
                canvas.DrawString(text, PCScreenFont.DefaultFont, color, x, y);
            else
                SmallTextRenderer.DrawClipped(canvas, text, x, y + 4, maxWidth, color);
        }

        internal static void Caption(Canvas canvas, string text, int x, int y,
            int maxWidth)
        {
            if (canvas != null && maxWidth > 0)
                SmallTextRenderer.DrawClipped(canvas, text, x, y, maxWidth, UiPalette.TextMuted);
        }
    }
}
