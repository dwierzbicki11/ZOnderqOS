using Cosmos.Kernel.Boot.Limine;

namespace ZonderqOS
{
    /// <summary>
    /// Raspberry Pi 4 stage-0 probe that bypasses Console/GUI entirely.
    /// BeforeRun paints the Limine framebuffer white; the first Run() paints
    /// a black marker in the top-left corner. This lets us distinguish managed
    /// execution from console/GOP issues on physical hardware without UART.
    /// </summary>
    public sealed unsafe class Rpi4BootKernel : Cosmos.Kernel.System.Kernel
    {
        private bool markerDrawn;

        protected override void BeforeRun()
        {
            FillFramebuffer(0xFF);
        }

        protected override void Run()
        {
            if (markerDrawn)
                return;

            DrawBlackMarker();
            markerDrawn = true;
        }

        private static LimineFramebuffer* GetFramebuffer()
        {
            LimineFramebufferResponse* response = Limine.Framebuffer.Response;
            if (response == null || response->FramebufferCount == 0 || response->Framebuffers == null)
                return null;

            LimineFramebuffer* framebuffer = response->Framebuffers[0];
            if (framebuffer == null || framebuffer->Address == null)
                return null;

            return framebuffer;
        }

        private static void FillFramebuffer(byte value)
        {
            LimineFramebuffer* framebuffer = GetFramebuffer();
            if (framebuffer == null)
                return;

            byte* pixels = (byte*)framebuffer->Address;
            ulong byteCount = framebuffer->Pitch * framebuffer->Height;

            for (ulong i = 0; i < byteCount; i++)
                pixels[i] = value;
        }

        private static void DrawBlackMarker()
        {
            LimineFramebuffer* framebuffer = GetFramebuffer();
            if (framebuffer == null)
                return;

            ulong bytesPerPixel = framebuffer->BitsPerPixel / 8UL;
            if (bytesPerPixel == 0)
                return;

            ulong markerWidth = framebuffer->Width / 4UL;
            ulong markerHeight = framebuffer->Height / 4UL;

            if (markerWidth > 256UL)
                markerWidth = 256UL;
            if (markerHeight > 256UL)
                markerHeight = 256UL;

            byte* pixels = (byte*)framebuffer->Address;
            ulong bytesPerMarkerRow = markerWidth * bytesPerPixel;

            for (ulong y = 0; y < markerHeight; y++)
            {
                byte* row = pixels + (y * framebuffer->Pitch);
                for (ulong x = 0; x < bytesPerMarkerRow; x++)
                    row[x] = 0;
            }
        }
    }
}
