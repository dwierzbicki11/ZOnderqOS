using Cosmos.Kernel.Boot.Limine;

namespace ZonderqOS
{
    /// <summary>
    /// Raspberry Pi 4 stage-1 probe.
    ///
    /// This deliberately bypasses Console, GUI, UART, interrupts and the normal
    /// Cosmos graphics stack. Text is drawn straight into the Limine framebuffer
    /// with a tiny built-in 5x7 font so every visible line is a boot milestone.
    /// </summary>
    public sealed unsafe class Rpi4BootKernel : Cosmos.Kernel.System.Kernel
    {
        private bool runAnnounced;

        protected override void BeforeRun()
        {
            LimineFramebuffer* framebuffer = GetFramebuffer();
            if (framebuffer == null)
                return;

            FillFramebuffer(framebuffer, 0xFF);

            ulong scale = framebuffer->Width >= 800UL ? 4UL : 3UL;
            ulong x = 32UL;
            ulong y = 32UL;
            ulong lineHeight = 9UL * scale;

            DrawText(framebuffer, "ZONDERQOS ARM64", x, y, scale);
            y += lineHeight;
            DrawText(framebuffer, "RPI4 FRAMEBUFFER OK", x, y, scale);
            y += lineHeight;
            DrawText(framebuffer, "BEFORERUN OK", x, y, scale);
            y += lineHeight;
            DrawText(framebuffer, "UART OFF", x, y, scale);
            y += lineHeight;
            DrawText(framebuffer, "IRQS OFF", x, y, scale);
        }

        protected override void Run()
        {
            if (runAnnounced)
                return;

            LimineFramebuffer* framebuffer = GetFramebuffer();
            if (framebuffer != null)
            {
                ulong scale = framebuffer->Width >= 800UL ? 4UL : 3UL;
                ulong y = 32UL + (5UL * 9UL * scale);
                DrawText(framebuffer, "RUN OK", 32UL, y, scale);
            }

            runAnnounced = true;
        }

        private static LimineFramebuffer* GetFramebuffer()
        {
            LimineFramebufferResponse* response = Limine.Framebuffer.Response;
            if (response == null || response->FramebufferCount == 0 || response->Framebuffers == null)
                return null;

            LimineFramebuffer* framebuffer = response->Framebuffers[0];
            if (framebuffer == null || framebuffer->Address == null)
                return null;

            if (framebuffer->BitsPerPixel < 8)
                return null;

            return framebuffer;
        }

        private static void FillFramebuffer(LimineFramebuffer* framebuffer, byte value)
        {
            byte* pixels = (byte*)framebuffer->Address;
            ulong byteCount = framebuffer->Pitch * framebuffer->Height;

            for (ulong i = 0; i < byteCount; i++)
                pixels[i] = value;
        }

        private static void DrawText(
            LimineFramebuffer* framebuffer,
            string text,
            ulong x,
            ulong y,
            ulong scale)
        {
            ulong cursorX = x;
            ulong advance = 6UL * scale;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == ' ')
                {
                    cursorX += advance;
                    continue;
                }

                DrawGlyph(framebuffer, c, cursorX, y, scale);
                cursorX += advance;
            }
        }

        private static void DrawGlyph(
            LimineFramebuffer* framebuffer,
            char c,
            ulong x,
            ulong y,
            ulong scale)
        {
            ulong glyph = GetGlyph(c);
            if (glyph == 0UL)
                return;

            for (ulong row = 0; row < 7UL; row++)
            {
                ulong rowBits = (glyph >> (int)(row * 5UL)) & 0x1FUL;

                for (ulong col = 0; col < 5UL; col++)
                {
                    ulong mask = 1UL << (int)(4UL - col);
                    if ((rowBits & mask) == 0)
                        continue;

                    DrawBlackBlock(
                        framebuffer,
                        x + (col * scale),
                        y + (row * scale),
                        scale,
                        scale);
                }
            }
        }

        private static void DrawBlackBlock(
            LimineFramebuffer* framebuffer,
            ulong x,
            ulong y,
            ulong width,
            ulong height)
        {
            if (x >= framebuffer->Width || y >= framebuffer->Height)
                return;

            ulong bytesPerPixel = framebuffer->BitsPerPixel / 8UL;
            if (bytesPerPixel == 0)
                return;

            ulong maxX = x + width;
            ulong maxY = y + height;

            if (maxX > framebuffer->Width)
                maxX = framebuffer->Width;
            if (maxY > framebuffer->Height)
                maxY = framebuffer->Height;

            byte* pixels = (byte*)framebuffer->Address;

            for (ulong py = y; py < maxY; py++)
            {
                byte* row = pixels + (py * framebuffer->Pitch);

                for (ulong px = x; px < maxX; px++)
                {
                    byte* pixel = row + (px * bytesPerPixel);
                    for (ulong b = 0; b < bytesPerPixel; b++)
                        pixel[b] = 0;
                }
            }
        }

        /// <summary>
        /// Returns seven packed 5-bit rows, least-significant row first.
        /// Only glyphs used by the stage-1 status screen are included.
        /// </summary>
        private static ulong GetGlyph(char c)
        {
            return c switch
            {
                'A' => 0x4631FC62EUL,
                'B' => 0x7A31F463EUL,
                'D' => 0x7A318C63EUL,
                'E' => 0x7E10F421FUL,
                'F' => 0x4210F421FUL,
                'I' => 0x7C842109FUL,
                'K' => 0x4654C5251UL,
                'M' => 0x4631AD771UL,
                'N' => 0x46319D731UL,
                'O' => 0x3A318C62EUL,
                'P' => 0x4210F463EUL,
                'Q' => 0x36558C62EUL,
                'R' => 0x4654F463EUL,
                'S' => 0x78217420FUL,
                'T' => 0x10842109FUL,
                'U' => 0x3A318C631UL,
                'Z' => 0x7E082083FUL,
                '4' => 0x085F928C2UL,
                '6' => 0x3A31F420EUL,
                _ => 0UL
            };
        }
    }
}
