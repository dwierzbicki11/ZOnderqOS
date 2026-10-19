using System.Collections.Generic;
using System.Drawing;

namespace Cosmos.Kernel.System.Graphics
{
    // Host-side drawing recorder; no Cosmos runtime or display required.
    public class Canvas
    {
        public readonly List<Rectangle> Rectangles = new List<Rectangle>();

        public void DrawFilledRectangle(Color color, int x, int y, int width, int height)
        {
            Rectangles.Add(new Rectangle(x, y, width, height));
        }
    }
}
