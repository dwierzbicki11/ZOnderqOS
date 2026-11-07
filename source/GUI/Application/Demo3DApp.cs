using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class Demo3DApp : Application
    {
        private struct Vec3
        {
            public float X, Y, Z;
            public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        }

        private struct Face
        {
            public int A, B, C;
            public Color BaseColor;
            public Face(int a, int b, int c, Color color) { A = a; B = b; C = c; BaseColor = color; }
        }

        private struct Edge
        {
            public int A, B;
            public Edge(int a, int b) { A = a; B = b; }
        }

        private static readonly Color Surface = Color.FromArgb(16, 20, 26);
        private static readonly Color ViewportBg = Color.FromArgb(8, 11, 15);
        private static readonly Color Panel = Color.FromArgb(24, 30, 38);
        private static readonly Color Border = Color.FromArgb(50, 62, 75);
        private static readonly Color TextColor = Color.FromArgb(234, 239, 244);
        private static readonly Color MutedColor = Color.FromArgb(132, 149, 164);

        private int currentShape; // 0: Cube, 1: Pyramid, 2: Diamond, 3: Torus
        private bool wireframeMode = true;
        private bool isRotating = true;

        private float rotX = 0.4f;
        private float rotY = 0.6f;
        private float rotZ = 0.0f;

        private int lastMouseX = -1;
        private int lastMouseY = -1;
        private bool isDragging;

        private long lastFpsTicks;
        private int frameCount;
        private int currentFps = 60;
        private long toneEndTicks;
        private readonly Action closeCallback;

        // Models data
        private Vec3[] vertices;
        private Face[] faces;
        private Edge[] edges;

        public Demo3DApp(int x, int y, Action onClose) : base("Demoscena 3D")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 480, 440, "Demoscena 3D Studio");
            Window.CloseAction = Close;
            LoadShape(0);
        }

        public override bool NeedsContinuousRedraw => isRotating || isDragging;

        public override void Close()
        {
            ZonderqOS.Hardware.Speaker.Stop();
            base.Close();
            closeCallback?.Invoke();
        }

        public override void Update()
        {
            long now = Stopwatch.GetTimestamp();

            if (toneEndTicks > 0 && now >= toneEndTicks)
            {
                ZonderqOS.Hardware.Speaker.Stop();
                toneEndTicks = 0;
            }

            if (isRotating && !isDragging)
            {
                rotX += 0.025f;
                rotY += 0.035f;
                rotZ += 0.015f;
            }

            // FPS calculation
            frameCount++;
            long freq = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;
            if (lastFpsTicks == 0) lastFpsTicks = now;
            if (now - lastFpsTicks >= freq)
            {
                currentFps = frameCount;
                frameCount = 0;
                lastFpsTicks = now;
            }
        }

        private void PlayTone(ushort freq, int durationMs)
        {
            ZonderqOS.Hardware.Speaker.Tone(freq);
            long clock = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;
            toneEndTicks = Stopwatch.GetTimestamp() + (clock * durationMs) / 1000;
        }

        private void LoadShape(int shape)
        {
            currentShape = shape;

            if (shape == 0) // Sześcian (Cube)
            {
                vertices = new Vec3[]
                {
                    new Vec3(-1, -1, -1), new Vec3(1, -1, -1), new Vec3(1, 1, -1), new Vec3(-1, 1, -1),
                    new Vec3(-1, -1,  1), new Vec3(1, -1,  1), new Vec3(1, 1,  1), new Vec3(-1, 1,  1)
                };

                edges = new Edge[]
                {
                    new Edge(0,1), new Edge(1,2), new Edge(2,3), new Edge(3,0),
                    new Edge(4,5), new Edge(5,6), new Edge(6,7), new Edge(7,4),
                    new Edge(0,4), new Edge(1,5), new Edge(2,6), new Edge(3,7)
                };

                faces = new Face[]
                {
                    // Front & Back
                    new Face(0, 1, 2, Color.FromArgb(64, 143, 204)), new Face(0, 2, 3, Color.FromArgb(64, 143, 204)),
                    new Face(5, 4, 7, Color.FromArgb(235, 65, 65)),  new Face(5, 7, 6, Color.FromArgb(235, 65, 65)),
                    // Top & Bottom
                    new Face(3, 2, 6, Color.FromArgb(60, 200, 100)), new Face(3, 6, 7, Color.FromArgb(60, 200, 100)),
                    new Face(4, 5, 1, Color.FromArgb(240, 195, 45)), new Face(4, 1, 0, Color.FromArgb(240, 195, 45)),
                    // Left & Right
                    new Face(4, 0, 3, Color.FromArgb(245, 55, 150)), new Face(4, 3, 7, Color.FromArgb(245, 55, 150)),
                    new Face(1, 5, 6, Color.FromArgb(90, 205, 240)), new Face(1, 6, 2, Color.FromArgb(90, 205, 240))
                };
            }
            else if (shape == 1) // Piramida (Pyramid)
            {
                vertices = new Vec3[]
                {
                    new Vec3(-1.1f, -0.8f, -1.1f), new Vec3(1.1f, -0.8f, -1.1f),
                    new Vec3(1.1f, -0.8f, 1.1f), new Vec3(-1.1f, -0.8f, 1.1f),
                    new Vec3(0, 1.2f, 0)
                };

                edges = new Edge[]
                {
                    new Edge(0,1), new Edge(1,2), new Edge(2,3), new Edge(3,0),
                    new Edge(0,4), new Edge(1,4), new Edge(2,4), new Edge(3,4)
                };

                faces = new Face[]
                {
                    new Face(0, 1, 4, Color.FromArgb(240, 195, 45)),
                    new Face(1, 2, 4, Color.FromArgb(235, 65, 65)),
                    new Face(2, 3, 4, Color.FromArgb(60, 200, 100)),
                    new Face(3, 0, 4, Color.FromArgb(64, 143, 204)),
                    new Face(3, 2, 1, Color.FromArgb(140, 100, 220)),
                    new Face(3, 1, 0, Color.FromArgb(140, 100, 220))
                };
            }
            else if (shape == 2) // Diament / Oktaedr (Diamond)
            {
                vertices = new Vec3[]
                {
                    new Vec3(0,  1.4f, 0),
                    new Vec3(-1.1f, 0, 0), new Vec3(0, 0, -1.1f),
                    new Vec3(1.1f, 0, 0), new Vec3(0, 0,  1.1f),
                    new Vec3(0, -1.4f, 0)
                };

                edges = new Edge[]
                {
                    new Edge(0,1), new Edge(0,2), new Edge(0,3), new Edge(0,4),
                    new Edge(1,2), new Edge(2,3), new Edge(3,4), new Edge(4,1),
                    new Edge(5,1), new Edge(5,2), new Edge(5,3), new Edge(5,4)
                };

                faces = new Face[]
                {
                    new Face(0, 1, 2, Color.FromArgb(90, 205, 240)), new Face(0, 2, 3, Color.FromArgb(64, 143, 204)),
                    new Face(0, 3, 4, Color.FromArgb(140, 100, 220)), new Face(0, 4, 1, Color.FromArgb(245, 55, 150)),
                    new Face(5, 2, 1, Color.FromArgb(60, 200, 100)), new Face(5, 3, 2, Color.FromArgb(240, 195, 45)),
                    new Face(5, 4, 3, Color.FromArgb(235, 65, 65)),  new Face(5, 1, 4, Color.FromArgb(220, 120, 40))
                };
            }
            else // Pierścień / Torus
            {
                int segs = 10;
                vertices = new Vec3[segs * 2];
                edges = new Edge[segs * 3];

                float r1 = 1.3f;
                float r2 = 0.7f;
                for (int i = 0; i < segs; i++)
                {
                    float angle = (i * 6.2831853f) / segs;
                    float cos = FastCos(angle);
                    float sin = FastSin(angle);

                    vertices[i] = new Vec3(r1 * cos, r1 * sin, -0.3f);
                    vertices[segs + i] = new Vec3(r2 * cos, r2 * sin, 0.3f);

                    int next = (i + 1) % segs;
                    edges[i * 3 + 0] = new Edge(i, next);
                    edges[i * 3 + 1] = new Edge(segs + i, segs + next);
                    edges[i * 3 + 2] = new Edge(i, segs + i);
                }

                faces = new Face[0]; // Wireframe only for complex torus
            }
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key == null)
                return;

            if (key.Key == ConsoleKeyEx.Escape)
            {
                Close();
                return;
            }

            if (key.Key == ConsoleKeyEx.Spacebar)
            {
                isRotating = !isRotating;
                PlayTone(500, 20);
                return;
            }

            if (key.Key == ConsoleKeyEx.Tab)
            {
                wireframeMode = !wireframeMode;
                PlayTone(600, 20);
                return;
            }

            if (key.Key == ConsoleKeyEx.D1) LoadShape(0);
            else if (key.Key == ConsoleKeyEx.D2) LoadShape(1);
            else if (key.Key == ConsoleKeyEx.D3) LoadShape(2);
            else if (key.Key == ConsoleKeyEx.D4) LoadShape(3);
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);

            if (!Window.Visible || Window.IsMinimized)
                return;

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;
            int vpX = contentX + 12;
            int vpY = contentY + 10;
            int vpW = 448;
            int vpH = 310;

            // Mouse drag inside viewport to rotate
            if (leftClicked)
            {
                if (mouseX >= vpX && mouseX < vpX + vpW && mouseY >= vpY && mouseY < vpY + vpH)
                {
                    if (leftWasClicked && lastMouseX >= 0 && lastMouseY >= 0)
                    {
                        int dx = mouseX - lastMouseX;
                        int dy = mouseY - lastMouseY;
                        rotY += dx * 0.015f;
                        rotX += dy * 0.015f;
                        isDragging = true;
                    }
                    lastMouseX = mouseX;
                    lastMouseY = mouseY;
                }
            }
            else
            {
                isDragging = false;
                lastMouseX = -1;
                lastMouseY = -1;
            }

            // Buttons Bar Hit-Testing (Bottom of window)
            int btnY = contentY + 330;
            int btnH = 26;

            if (leftClicked && !leftWasClicked)
            {
                // Shape 1: Sześcian
                if (Hit(mouseX, mouseY, contentX + 14, btnY, 74, btnH)) { LoadShape(0); PlayTone(650, 25); return; }
                // Shape 2: Piramida
                if (Hit(mouseX, mouseY, contentX + 94, btnY, 74, btnH)) { LoadShape(1); PlayTone(700, 25); return; }
                // Shape 3: Diament
                if (Hit(mouseX, mouseY, contentX + 174, btnY, 74, btnH)) { LoadShape(2); PlayTone(750, 25); return; }
                // Shape 4: Torus
                if (Hit(mouseX, mouseY, contentX + 254, btnY, 74, btnH)) { LoadShape(3); PlayTone(800, 25); return; }

                // Wireframe / Shaded toggle
                if (Hit(mouseX, mouseY, contentX + 336, btnY, 124, btnH))
                {
                    wireframeMode = !wireframeMode;
                    PlayTone(600, 25);
                    return;
                }

                // Pause / Rotate toggle
                if (Hit(mouseX, mouseY, contentX + 14, btnY + 32, 100, 22))
                {
                    isRotating = !isRotating;
                    PlayTone(520, 20);
                    return;
                }
            }
        }

        private static bool Hit(int mx, int my, int x, int y, int w, int h)
        {
            return mx >= x && mx < x + w && my >= y && my < y + h;
        }

        public override void Render(Canvas canvas)
        {
            if (!Window.Visible || Window.IsMinimized)
                return;

            base.Render(canvas);

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;
            int contentWidth = Math.Max(1, Window.Width - 16);
            int contentHeight = Math.Max(1, Window.Height - 46);

            // Window surface
            canvas.DrawFilledRectangle(Surface, contentX, contentY, contentWidth, contentHeight);

            // Viewport
            int vpX = contentX + 12;
            int vpY = contentY + 10;
            int vpW = contentWidth - 24;
            int vpH = 310;

            canvas.DrawFilledRectangle(ViewportBg, vpX, vpY, vpW, vpH);
            canvas.DrawRectangle(Border, vpX - 1, vpY - 1, vpW + 2, vpH + 2);

            // Grid lines on viewport background
            Color gridLine = Color.FromArgb(14, 19, 26);
            for (int gx = vpX + 32; gx < vpX + vpW; gx += 32)
                canvas.DrawFilledRectangle(gridLine, gx, vpY, 1, vpH);
            for (int gy = vpY + 31; gy < vpY + vpH; gy += 31)
                canvas.DrawFilledRectangle(gridLine, vpX, gy, vpW, 1);

            // Transform vertices: 3D Rotations + Perspective Projection
            float sinX = FastSin(rotX), cosX = FastCos(rotX);
            float sinY = FastSin(rotY), cosY = FastCos(rotY);
            float sinZ = FastSin(rotZ), cosZ = FastCos(rotZ);

            int centerX = vpX + vpW / 2;
            int centerY = vpY + vpH / 2;
            float fov = 175.0f;
            float cameraDist = 3.6f;

            int count = vertices.Length;
            int[] screenX = new int[count];
            int[] screenY = new int[count];
            float[] depthZ = new float[count];

            for (int i = 0; i < count; i++)
            {
                Vec3 v = vertices[i];

                // Rotate around Y
                float x1 = v.X * cosY - v.Z * sinY;
                float z1 = v.X * sinY + v.Z * cosY;

                // Rotate around X
                float y2 = v.Y * cosX - z1 * sinX;
                float z2 = v.Y * sinX + z1 * cosX;

                // Rotate around Z
                float x3 = x1 * cosZ - y2 * sinZ;
                float y3 = x1 * sinZ + y2 * cosZ;

                depthZ[i] = z2 + cameraDist;
                float invZ = 1.0f / Math.Max(0.5f, depthZ[i]);

                screenX[i] = centerX + (int)(x3 * fov * invZ);
                screenY[i] = centerY - (int)(y3 * fov * invZ);
            }

            // Draw Flat Shaded Faces or Wireframe
            if (!wireframeMode && faces != null && faces.Length > 0)
            {
                for (int f = 0; f < faces.Length; f++)
                {
                    Face face = faces[f];
                    int ax = screenX[face.A], ay = screenY[face.A];
                    int bx = screenX[face.B], by = screenY[face.B];
                    int cx = screenX[face.C], cy = screenY[face.C];

                    // 2D Normal Z (Backface culling)
                    int crossZ = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
                    if (crossZ > 0)
                    {
                        // Simple flat shading based on orientation
                        float intensity = 0.55f + Math.Min(0.45f, Math.Max(0.0f, crossZ / 12000.0f));
                        Color col = Color.FromArgb(
                            (int)(face.BaseColor.R * intensity),
                            (int)(face.BaseColor.G * intensity),
                            (int)(face.BaseColor.B * intensity));

                        DrawFilledTriangle(canvas, ax, ay, bx, by, cx, cy, col);
                        canvas.DrawLine(Border, ax, ay, bx, by);
                        canvas.DrawLine(Border, bx, by, cx, cy);
                        canvas.DrawLine(Border, cx, cy, ax, ay);
                    }
                }
            }
            else if (edges != null)
            {
                Color edgeColor = SystemTheme.Accent;
                for (int e = 0; e < edges.Length; e++)
                {
                    Edge edge = edges[e];
                    canvas.DrawLine(edgeColor, screenX[edge.A], screenY[edge.A], screenX[edge.B], screenY[edge.B]);
                }

                // Draw vertex dots
                for (int i = 0; i < count; i++)
                {
                    canvas.DrawFilledRectangle(Color.White, screenX[i] - 2, screenY[i] - 2, 4, 4);
                }
            }

            // Overlay Info (Top Left of Viewport)
            SmallTextRenderer.Draw(canvas, "3D SOFTWARE ENGINE", vpX + 10, vpY + 10, SystemTheme.Accent);
            SmallTextRenderer.Draw(canvas, "FPS: " + currentFps + "  Wierzcholki: " + count, vpX + 10, vpY + 24, MutedColor);

            // Controls Bar
            int btnY = contentY + 330;
            int btnH = 26;

            DrawBtn(canvas, "SZESCIAN", contentX + 14, btnY, 74, btnH, currentShape == 0);
            DrawBtn(canvas, "PIRAMIDA", contentX + 94, btnY, 74, btnH, currentShape == 1);
            DrawBtn(canvas, "DIAMENT", contentX + 174, btnY, 74, btnH, currentShape == 2);
            DrawBtn(canvas, "TORUS", contentX + 254, btnY, 74, btnH, currentShape == 3);

            string modeLabel = wireframeMode ? "TRYB: SIATKA" : "TRYB: SCIANKI";
            DrawBtn(canvas, modeLabel, contentX + 336, btnY, 124, btnH, false);

            string rotLabel = isRotating ? "OBROT: WL" : "OBROT: STOP";
            DrawBtn(canvas, rotLabel, contentX + 14, btnY + 32, 94, 22, isRotating);

            SmallTextRenderer.Draw(canvas, "Chwyc myszka aby obracac recznie  Spacja: Pauza  Tab: Tryb",
                contentX + 118, btnY + 37, MutedColor);
        }

        private static void DrawBtn(Canvas canvas, string text, int x, int y, int w, int h, bool active)
        {
            Color bg = active ? Color.FromArgb(40, 52, 68) : Panel;
            Color border = active ? SystemTheme.Accent : Border;
            canvas.DrawFilledRectangle(bg, x, y, w, h);
            canvas.DrawRectangle(border, x, y, w, h);
            SmallTextRenderer.DrawCentered(canvas, text, x, y + (h - 7) / 2, w, active ? SystemTheme.Accent : TextColor);
        }

        private static void DrawFilledTriangle(Canvas canvas, int x1, int y1, int x2, int y2, int x3, int y3, Color color)
        {
            // Scanline triangle fill
            if (y1 > y2) { Swap(ref x1, ref x2); Swap(ref y1, ref y2); }
            if (y1 > y3) { Swap(ref x1, ref x3); Swap(ref y1, ref y3); }
            if (y2 > y3) { Swap(ref x2, ref x3); Swap(ref y2, ref y3); }

            int totalH = y3 - y1;
            if (totalH == 0) return;

            for (int y = y1; y <= y3; y++)
            {
                bool secondHalf = y > y2 || y2 == y1;
                int segmentH = secondHalf ? y3 - y2 : y2 - y1;
                if (segmentH == 0) continue;

                float alpha = (float)(y - y1) / totalH;
                float beta = (float)(y - (secondHalf ? y2 : y1)) / segmentH;

                int ax = x1 + (int)((x3 - x1) * alpha);
                int bx = secondHalf ? x2 + (int)((x3 - x2) * beta) : x1 + (int)((x2 - x1) * beta);

                if (ax > bx) Swap(ref ax, ref bx);
                canvas.DrawFilledRectangle(color, ax, y, Math.Max(1, bx - ax + 1), 1);
            }
        }

        private static void Swap(ref int a, ref int b)
        {
            int t = a; a = b; b = t;
        }

        private static float FastSin(float rad)
        {
            const float PI = 3.14159265f;
            const float TWO_PI = 6.2831853f;
            rad = rad % TWO_PI;
            if (rad > PI) rad -= TWO_PI;
            if (rad < -PI) rad += TWO_PI;

            float abs = rad < 0 ? -rad : rad;
            float num = 16.0f * rad * (PI - abs);
            float den = 5.0f * PI * PI - 4.0f * abs * (PI - abs);
            return den != 0 ? num / den : 0;
        }

        private static float FastCos(float rad) => FastSin(rad + 1.57079632f);
    }
}
