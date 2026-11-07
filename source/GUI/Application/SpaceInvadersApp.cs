using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class SpaceInvadersApp : Application
    {
        private static readonly Color BgColor = Color.FromArgb(8, 10, 16);
        private static readonly Color HeaderBg = Color.FromArgb(16, 20, 30);
        private static readonly Color BorderColor = Color.FromArgb(48, 60, 76);
        private static readonly Color TextColor = Color.FromArgb(235, 240, 248);
        private static readonly Color MutedColor = Color.FromArgb(125, 140, 155);
        private static readonly Color AccentColor = Color.FromArgb(70, 160, 225);
        private static readonly Color PlayerColor = Color.FromArgb(60, 220, 100);
        private static readonly Color AlienRed = Color.FromArgb(240, 70, 70);
        private static readonly Color AlienYellow = Color.FromArgb(245, 205, 50);
        private static readonly Color AlienCyan = Color.FromArgb(60, 210, 240);
        private static readonly Color BunkerColor = Color.FromArgb(70, 190, 110);
        private static readonly Color BulletPlayer = Color.FromArgb(255, 255, 120);
        private static readonly Color BulletAlien = Color.FromArgb(255, 100, 100);

        private const int BoardWidth = 480;
        private const int BoardHeight = 360;

        // Player state
        private int playerX = 220;
        private int playerY = 325;
        private const int PlayerWidth = 26;
        private const int PlayerHeight = 14;
        private int playerSpeed = 5;
        private int lives = 3;
        private int score = 0;
        private int highScore = 1000;
        private int wave = 1;

        // Player bullet
        private bool playerBulletActive = false;
        private int playerBulletX;
        private int playerBulletY;
        private const int PlayerBulletSpeed = 8;

        // Alien bullets (up to 3 simultaneous)
        private struct AlienBullet
        {
            public bool Active;
            public int X;
            public int Y;
        }
        private readonly AlienBullet[] alienBullets = new AlienBullet[3];
        private const int AlienBulletSpeed = 4;
        private int alienShootCooldown = 35;

        // Aliens fleet (3 rows of 8 aliens = 24 aliens)
        private const int AlienRows = 3;
        private const int AlienCols = 8;
        private struct Alien
        {
            public bool Alive;
            public int X;
            public int Y;
            public int Type; // 0 = Red (top), 1 = Yellow (mid), 2 = Cyan (bottom)
        }
        private readonly Alien[] aliens = new Alien[AlienRows * AlienCols];
        private int alienStepX = 6;
        private int alienStepY = 12;
        private int alienDirection = 1; // 1 = right, -1 = left
        private int alienMoveCooldown = 20;
        private int alienMoveTimer = 0;
        private int alienAnimFrame = 0;
        private int aliveAlienCount = 24;

        // March sound notes (4-note descending classic pulse)
        private static readonly ushort[] MarchNotes = { 180, 160, 140, 120 };
        private int marchStep = 0;

        // Bunkers (3 bunkers, each 4 columns x 3 rows of destructible blocks)
        private const int BunkerCount = 3;
        private const int BunkerBlockCols = 5;
        private const int BunkerBlockRows = 3;
        private const int BlockW = 7;
        private const int BlockH = 6;
        private readonly bool[,,] bunkers = new bool[BunkerCount, BunkerBlockRows, BunkerBlockCols];

        // Game status: 0 = Playing, 1 = WaveClear, 2 = GameOver, 3 = Paused
        private int gameState = 0;
        private long toneEndTicks;
        private int waveClearTimer = 0;

        private readonly Action closeCallback;

        // Pixel-art sprite definitions (11x8)
        // 1 = pixel on, 0 = pixel off
        private static readonly byte[,] SpriteTop1 = {
            {0,0,0,1,1,0,0,0},
            {0,0,1,1,1,1,0,0},
            {0,1,1,1,1,1,1,0},
            {1,1,0,1,1,0,1,1},
            {1,1,1,1,1,1,1,1},
            {0,0,1,0,0,1,0,0},
            {0,1,0,1,1,0,1,0},
            {1,0,1,0,0,1,0,1}
        };

        private static readonly byte[,] SpriteTop2 = {
            {0,0,0,1,1,0,0,0},
            {0,0,1,1,1,1,0,0},
            {0,1,1,1,1,1,1,0},
            {1,1,0,1,1,0,1,1},
            {1,1,1,1,1,1,1,1},
            {0,1,0,1,1,0,1,0},
            {1,0,0,0,0,0,0,1},
            {0,1,0,0,0,0,1,0}
        };

        public SpaceInvadersApp(int x, int y, Action onClose) : base("Space Invaders")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 500, 420, "Space Invaders - Kosmiczni Najeźdźcy");
            Window.CloseAction = Close;

            InitWave();
        }

        public override bool NeedsContinuousRedraw => gameState == 0 || toneEndTicks > 0;

        public override void Close()
        {
            ZonderqOS.Hardware.Speaker.Stop();
            base.Close();
            closeCallback?.Invoke();
        }

        private void InitWave()
        {
            aliveAlienCount = AlienRows * AlienCols;
            marchStep = 0;
            alienDirection = 1;
            alienMoveTimer = 0;
            alienMoveCooldown = Math.Max(6, 22 - wave * 2);

            int startX = 35;
            int startY = 30;
            int spacingX = 46;
            int spacingY = 28;

            for (int r = 0; r < AlienRows; r++)
            {
                for (int c = 0; c < AlienCols; c++)
                {
                    int idx = r * AlienCols + c;
                    aliens[idx].Alive = true;
                    aliens[idx].X = startX + c * spacingX;
                    aliens[idx].Y = startY + r * spacingY;
                    aliens[idx].Type = r; // 0 = Red, 1 = Yellow, 2 = Cyan
                }
            }

            // Init Bunkers
            for (int b = 0; b < BunkerCount; b++)
            {
                for (int r = 0; r < BunkerBlockRows; r++)
                {
                    for (int c = 0; c < BunkerBlockCols; c++)
                    {
                        // Cutout bottom middle arch in bunker
                        if (r == BunkerBlockRows - 1 && c == 2)
                            bunkers[b, r, c] = false;
                        else
                            bunkers[b, r, c] = true;
                    }
                }
            }

            playerBulletActive = false;
            for (int i = 0; i < alienBullets.Length; i++)
                alienBullets[i].Active = false;

            playerX = BoardWidth / 2 - PlayerWidth / 2;
        }

        private void PlayTone(ushort freq, int ms)
        {
            ZonderqOS.Hardware.Speaker.Tone(freq);
            long clock = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;
            toneEndTicks = Stopwatch.GetTimestamp() + (clock * ms) / 1000;
        }

        public override void Update()
        {
            long now = Stopwatch.GetTimestamp();
            if (toneEndTicks > 0 && now >= toneEndTicks)
            {
                ZonderqOS.Hardware.Speaker.Stop();
                toneEndTicks = 0;
            }

            if (gameState == 1) // Wave Clear
            {
                waveClearTimer++;
                if (waveClearTimer > 50)
                {
                    wave++;
                    InitWave();
                    gameState = 0;
                }
                return;
            }

            if (gameState != 0) // GameOver or Paused
                return;

            // Player movement
            if (keyLeft && playerX > 10)
                playerX -= playerSpeed;
            if (keyRight && playerX < BoardWidth - PlayerWidth - 10)
                playerX += playerSpeed;

            // Player shoot
            if (keyShoot && !playerBulletActive)
            {
                playerBulletActive = true;
                playerBulletX = playerX + PlayerWidth / 2 - 1;
                playerBulletY = playerY - 4;
                PlayTone(880, 25);
            }

            // Update player bullet
            if (playerBulletActive)
            {
                playerBulletY -= PlayerBulletSpeed;
                if (playerBulletY < 5)
                {
                    playerBulletActive = false;
                }
                else
                {
                    // Check collision with bunkers
                    if (CheckBulletBunkerCollision(playerBulletX, playerBulletY))
                    {
                        playerBulletActive = false;
                    }
                    // Check collision with aliens
                    else
                    {
                        for (int i = 0; i < aliens.Length; i++)
                        {
                            if (!aliens[i].Alive) continue;

                            if (playerBulletX >= aliens[i].X && playerBulletX <= aliens[i].X + 26 &&
                                playerBulletY >= aliens[i].Y && playerBulletY <= aliens[i].Y + 18)
                            {
                                aliens[i].Alive = false;
                                playerBulletActive = false;
                                aliveAlienCount--;

                                int pts = (3 - aliens[i].Type) * 10;
                                score += pts;
                                if (score > highScore) highScore = score;

                                PlayTone(260, 40);

                                // Check wave clear
                                if (aliveAlienCount <= 0)
                                {
                                    gameState = 1; // Wave Clear
                                    waveClearTimer = 0;
                                    PlayTone(784, 180);
                                    return;
                                }

                                // Speed up aliens as they diminish
                                alienMoveCooldown = Math.Max(3, (aliveAlienCount * 20) / (AlienRows * AlienCols));
                                break;
                            }
                        }
                    }
                }
            }

            // Aliens movement
            alienMoveTimer++;
            if (alienMoveTimer >= alienMoveCooldown)
            {
                alienMoveTimer = 0;
                alienAnimFrame = 1 - alienAnimFrame;

                // Play classic 4-note heartbeat
                PlayTone(MarchNotes[marchStep], 25);
                marchStep = (marchStep + 1) % MarchNotes.Length;

                // Check edge collision
                bool touchEdge = false;
                for (int i = 0; i < aliens.Length; i++)
                {
                    if (!aliens[i].Alive) continue;
                    if (alienDirection == 1 && aliens[i].X >= BoardWidth - 36)
                    {
                        touchEdge = true;
                        break;
                    }
                    else if (alienDirection == -1 && aliens[i].X <= 12)
                    {
                        touchEdge = true;
                        break;
                    }
                }

                if (touchEdge)
                {
                    alienDirection = -alienDirection;
                    for (int i = 0; i < aliens.Length; i++)
                    {
                        aliens[i].Y += alienStepY;
                        // If aliens reach player's level -> Game Over!
                        if (aliens[i].Alive && aliens[i].Y >= playerY - 16)
                        {
                            GameOver();
                            return;
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < aliens.Length; i++)
                    {
                        aliens[i].X += alienStepX * alienDirection;
                    }
                }
            }

            // Alien shooting
            alienShootCooldown--;
            if (alienShootCooldown <= 0)
            {
                alienShootCooldown = Math.Max(18, 40 - wave * 4);
                // Find a random alive alien in bottom active position
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    int col = (now.GetHashCode() + attempt * 7) & 0x7; // 0..7
                    int chosenAlien = -1;
                    for (int r = AlienRows - 1; r >= 0; r--)
                    {
                        int idx = r * AlienCols + col;
                        if (aliens[idx].Alive)
                        {
                            chosenAlien = idx;
                            break;
                        }
                    }

                    if (chosenAlien >= 0)
                    {
                        // Spawn alien bullet in free slot
                        for (int b = 0; b < alienBullets.Length; b++)
                        {
                            if (!alienBullets[b].Active)
                            {
                                alienBullets[b].Active = true;
                                alienBullets[b].X = aliens[chosenAlien].X + 12;
                                alienBullets[b].Y = aliens[chosenAlien].Y + 18;
                                break;
                            }
                        }
                        break;
                    }
                }
            }

            // Update alien bullets
            for (int b = 0; b < alienBullets.Length; b++)
            {
                if (!alienBullets[b].Active) continue;

                alienBullets[b].Y += AlienBulletSpeed;
                if (alienBullets[b].Y > BoardHeight - 5)
                {
                    alienBullets[b].Active = false;
                }
                else
                {
                    // Check collision with bunkers
                    if (CheckBulletBunkerCollision(alienBullets[b].X, alienBullets[b].Y))
                    {
                        alienBullets[b].Active = false;
                    }
                    // Check collision with player
                    else if (alienBullets[b].X >= playerX && alienBullets[b].X <= playerX + PlayerWidth &&
                             alienBullets[b].Y >= playerY && alienBullets[b].Y <= playerY + PlayerHeight)
                    {
                        alienBullets[b].Active = false;
                        lives--;
                        PlayTone(140, 150);

                        if (lives <= 0)
                        {
                            GameOver();
                            return;
                        }
                        else
                        {
                            playerX = BoardWidth / 2 - PlayerWidth / 2;
                        }
                    }
                }
            }
        }

        private bool CheckBulletBunkerCollision(int bx, int by)
        {
            int bunkerStartX = 65;
            int bunkerSpacing = 145;

            for (int b = 0; b < BunkerCount; b++)
            {
                int bX = bunkerStartX + b * bunkerSpacing;
                int bY = 275;

                if (bx >= bX && bx < bX + BunkerBlockCols * BlockW &&
                    by >= bY && by < bY + BunkerBlockRows * BlockH)
                {
                    int c = (bx - bX) / BlockW;
                    int r = (by - bY) / BlockH;

                    if (r >= 0 && r < BunkerBlockRows && c >= 0 && c < BunkerBlockCols && bunkers[b, r, c])
                    {
                        bunkers[b, r, c] = false;
                        return true;
                    }
                }
            }
            return false;
        }

        private void GameOver()
        {
            gameState = 2; // Game Over
            PlayTone(110, 250);
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

            if (key.Key == ConsoleKeyEx.P)
            {
                if (gameState == 0) gameState = 3; // Pause
                else if (gameState == 3) gameState = 0; // Resume
                return;
            }

            if (key.Key == ConsoleKeyEx.R && (gameState == 2 || gameState == 1))
            {
                score = 0;
                lives = 3;
                wave = 1;
                InitWave();
                gameState = 0;
                return;
            }

            // Movement keys
            if (key.Key == ConsoleKeyEx.LeftArrow || key.KeyChar == 'a' || key.KeyChar == 'A')
            {
                playerX = Math.Max(10, playerX - 10);
            }
            else if (key.Key == ConsoleKeyEx.RightArrow || key.KeyChar == 'd' || key.KeyChar == 'D')
            {
                playerX = Math.Min(BoardWidth - PlayerWidth - 10, playerX + 10);
            }

            // Shoot
            if (key.Key == ConsoleKeyEx.Spacebar)
            {
                if (!playerBulletActive && gameState == 0)
                {
                    playerBulletActive = true;
                    playerBulletX = playerX + PlayerWidth / 2 - 1;
                    playerBulletY = playerY - 4;
                    PlayTone(880, 25);
                }
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);

            if (!Window.Visible || Window.IsMinimized)
                return;

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;

            if (leftClicked && !leftWasClicked)
            {
                // In game over or wave clear: click to restart
                if (gameState == 2)
                {
                    score = 0;
                    lives = 3;
                    wave = 1;
                    InitWave();
                    gameState = 0;
                    return;
                }

                // Mouse steer and shoot
                int boardRelX = mouseX - contentX;
                if (boardRelX >= 0 && boardRelX < BoardWidth)
                {
                    playerX = Math.Max(10, Math.Min(BoardWidth - PlayerWidth - 10, boardRelX - PlayerWidth / 2));
                    if (!playerBulletActive && gameState == 0)
                    {
                        playerBulletActive = true;
                        playerBulletX = playerX + PlayerWidth / 2 - 1;
                        playerBulletY = playerY - 4;
                        PlayTone(880, 25);
                    }
                }
            }
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

            // Game board canvas
            canvas.DrawFilledRectangle(BgColor, contentX, contentY, contentWidth, contentHeight);

            // Top Status Bar
            canvas.DrawFilledRectangle(HeaderBg, contentX, contentY, contentWidth, 26);
            canvas.DrawLine(BorderColor, contentX, contentY + 26, contentX + contentWidth, contentY + 26);

            string scoreStr = "WYNIK: " + score.ToString("00000");
            string highStr = "REKORD: " + highScore.ToString("00000");
            string waveStr = "FALA " + wave;
            SmallTextRenderer.Draw(canvas, scoreStr, contentX + 12, contentY + 9, TextColor);
            SmallTextRenderer.Draw(canvas, highStr, contentX + 160, contentY + 9, AccentColor);
            SmallTextRenderer.Draw(canvas, waveStr, contentX + 310, contentY + 9, AlienYellow);

            // Lives indicators
            SmallTextRenderer.Draw(canvas, "ZYCIA:", contentX + 395, contentY + 9, MutedColor);
            for (int l = 0; l < lives; l++)
            {
                int lx = contentX + 445 + l * 14;
                canvas.DrawFilledRectangle(PlayerColor, lx, contentY + 9, 8, 5);
                canvas.DrawFilledRectangle(PlayerColor, lx + 3, contentY + 7, 2, 2);
            }

            int boardY = contentY + 28;

            // Render Aliens
            for (int i = 0; i < aliens.Length; i++)
            {
                if (!aliens[i].Alive) continue;

                int ax = contentX + aliens[i].X;
                int ay = boardY + aliens[i].Y;
                Color col = aliens[i].Type == 0 ? AlienRed : (aliens[i].Type == 1 ? AlienYellow : AlienCyan);

                RenderAlienSprite(canvas, ax, ay, aliens[i].Type, alienAnimFrame, col);
            }

            // Render Bunkers
            int bunkerStartX = contentX + 65;
            int bunkerSpacing = 145;
            for (int b = 0; b < BunkerCount; b++)
            {
                int bx = bunkerStartX + b * bunkerSpacing;
                int by = boardY + 275;

                for (int r = 0; r < BunkerBlockRows; r++)
                {
                    for (int c = 0; c < BunkerBlockCols; c++)
                    {
                        if (bunkers[b, r, c])
                        {
                            canvas.DrawFilledRectangle(BunkerColor, bx + c * BlockW, by + r * BlockH, BlockW - 1, BlockH - 1);
                        }
                    }
                }
            }

            // Render Player
            int px = contentX + playerX;
            int py = boardY + playerY;
            // Player base
            canvas.DrawFilledRectangle(PlayerColor, px, py + 6, PlayerWidth, 8);
            canvas.DrawFilledRectangle(PlayerColor, px + 4, py + 3, PlayerWidth - 8, 3);
            canvas.DrawFilledRectangle(PlayerColor, px + PlayerWidth / 2 - 2, py, 4, 3);

            // Render Player Bullet
            if (playerBulletActive)
            {
                canvas.DrawFilledRectangle(BulletPlayer, contentX + playerBulletX, boardY + playerBulletY, 2, 7);
            }

            // Render Alien Bullets
            for (int b = 0; b < alienBullets.Length; b++)
            {
                if (alienBullets[b].Active)
                {
                    canvas.DrawFilledRectangle(BulletAlien, contentX + alienBullets[b].X, boardY + alienBullets[b].Y, 2, 6);
                }
            }

            // Bottom ground green line
            canvas.DrawLine(Color.FromArgb(40, 160, 70), contentX, boardY + BoardHeight - 2,
                contentX + contentWidth, boardY + BoardHeight - 2);

            // Overlays: GameOver, WaveClear, Paused
            if (gameState == 2) // Game Over
            {
                int boxW = 240;
                int boxH = 90;
                int boxX = contentX + (contentWidth - boxW) / 2;
                int boxY = boardY + 110;

                canvas.DrawFilledRectangle(Color.FromArgb(20, 14, 22), boxX, boxY, boxW, boxH);
                canvas.DrawRectangle(AlienRed, boxX, boxY, boxW, boxH);
                SmallTextRenderer.DrawCentered(canvas, "KONIEC GRY!", boxX, boxY + 18, boxW, AlienRed);
                SmallTextRenderer.DrawCentered(canvas, "WYNIK: " + score, boxX, boxY + 40, boxW, TextColor);
                SmallTextRenderer.DrawCentered(canvas, "Kliknij lub wcisnij [R] aby zagrac ponownie", boxX, boxY + 62, boxW, MutedColor);
            }
            else if (gameState == 1) // Wave Cleared
            {
                int boxW = 220;
                int boxH = 65;
                int boxX = contentX + (contentWidth - boxW) / 2;
                int boxY = boardY + 120;

                canvas.DrawFilledRectangle(Color.FromArgb(14, 24, 18), boxX, boxY, boxW, boxH);
                canvas.DrawRectangle(PlayerColor, boxX, boxY, boxW, boxH);
                SmallTextRenderer.DrawCentered(canvas, "FALA " + wave + " UKONCZONA!", boxX, boxY + 18, boxW, PlayerColor);
                SmallTextRenderer.DrawCentered(canvas, "Przygotuj sie na nastepna fale...", boxX, boxY + 40, boxW, TextColor);
            }
            else if (gameState == 3) // Paused
            {
                int boxW = 180;
                int boxH = 50;
                int boxX = contentX + (contentWidth - boxW) / 2;
                int boxY = boardY + 130;

                canvas.DrawFilledRectangle(Color.FromArgb(20, 26, 36), boxX, boxY, boxW, boxH);
                canvas.DrawRectangle(AccentColor, boxX, boxY, boxW, boxH);
                SmallTextRenderer.DrawCentered(canvas, "PAUZA", boxX, boxY + 20, boxW, AccentColor);
            }
        }

        private static void RenderAlienSprite(Canvas canvas, int x, int y, int type, int frame, Color color)
        {
            // 24x16 scaled alien
            const int scale = 2;
            byte[,] sprite = (frame == 0) ? SpriteTop1 : SpriteTop2;

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    if (sprite[r, c] == 1)
                    {
                        canvas.DrawFilledRectangle(color, x + (c + 2) * scale, y + r * scale, scale, scale);
                    }
                }
            }
        }
    }
}
