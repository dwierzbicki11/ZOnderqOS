using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class SolitaireApp : Application
    {
        public struct Card
        {
            public int Suit;  // 0: Hearts (♥), 1: Diamonds (♦), 2: Clubs (♣), 3: Spades (♠)
            public int Value; // 1 (Ace) to 13 (King)
            public bool IsFaceUp;

            public Card(int suit, int value, bool isFaceUp)
            {
                Suit = suit;
                Value = value;
                IsFaceUp = isFaceUp;
            }

            public bool IsRed => Suit == 0 || Suit == 1;

            public string RankString
            {
                get
                {
                    return Value switch
                    {
                        1 => "A",
                        11 => "J",
                        12 => "Q",
                        13 => "K",
                        _ => Value.ToString()
                    };
                }
            }

            public string SuitSymbol
            {
                get
                {
                    return Suit switch
                    {
                        0 => "H", // Hearts
                        1 => "D", // Diamonds
                        2 => "C", // Clubs
                        _ => "S"  // Spades
                    };
                }
            }

            public Color SuitColor => IsRed ? Color.FromArgb(220, 35, 35) : Color.FromArgb(20, 25, 30);
        }

        private const int CardW = 46;
        private const int CardH = 62;
        private const int ColGap = 10;
        private const int FaceDownStep = 12;
        private const int FaceUpStep = 18;

        private static readonly Color TableBg = Color.FromArgb(18, 54, 32);     // Classic green felt
        private static readonly Color HeaderPanel = Color.FromArgb(14, 42, 25);
        private static readonly Color SlotBorder = Color.FromArgb(32, 85, 50);
        private static readonly Color CardBg = Color.FromArgb(250, 250, 252);
        private static readonly Color CardBackBg = Color.FromArgb(28, 62, 125);
        private static readonly Color CardBackBorder = Color.FromArgb(70, 120, 210);
        private static readonly Color CardBorder = Color.FromArgb(130, 135, 145);
        private static readonly Color SelectBorder = Color.FromArgb(250, 215, 60); // Gold
        private static readonly Color TextColor = Color.FromArgb(234, 239, 244);
        private static readonly Color MutedColor = Color.FromArgb(140, 185, 155);

        private readonly List<Card> stock = new List<Card>(24);
        private readonly List<Card> waste = new List<Card>(24);
        private readonly List<Card>[] foundations = new List<Card>[4];
        private readonly List<Card>[] tableau = new List<Card>[7];

        private int selSource = -1; // -1: none, 0: waste, 1..7: tableau 0..6, 10..13: foundation 0..3
        private int selIndex = -1;  // card index within pile

        private uint score;
        private int moves;
        private bool isWon;

        private long toneEndTicks;
        private readonly Random rng = new Random();
        private readonly Action closeCallback;

        public SolitaireApp(int x, int y, Action onClose) : base("Pasjans")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 540, 470, "Pasjans Klondike");
            Window.CloseAction = Close;

            for (int i = 0; i < 4; i++) foundations[i] = new List<Card>(13);
            for (int i = 0; i < 7; i++) tableau[i] = new List<Card>(19);

            ResetGame();
        }

        public override bool NeedsContinuousRedraw => isWon;

        public override void Close()
        {
            ZonderqOS.Hardware.Speaker.Stop();
            base.Close();
            closeCallback?.Invoke();
        }

        public override void Update()
        {
            if (toneEndTicks > 0 && Stopwatch.GetTimestamp() >= toneEndTicks)
            {
                ZonderqOS.Hardware.Speaker.Stop();
                toneEndTicks = 0;
            }
        }

        private void PlayTone(ushort freq, int durationMs)
        {
            ZonderqOS.Hardware.Speaker.Tone(freq);
            long clock = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;
            toneEndTicks = Stopwatch.GetTimestamp() + (clock * durationMs) / 1000;
        }

        private void ResetGame()
        {
            stock.Clear();
            waste.Clear();
            for (int i = 0; i < 4; i++) foundations[i].Clear();
            for (int i = 0; i < 7; i++) tableau[i].Clear();

            selSource = -1;
            selIndex = -1;
            score = 0;
            moves = 0;
            isWon = false;

            // Generate 52 cards
            List<Card> deck = new List<Card>(52);
            for (int s = 0; s < 4; s++)
            {
                for (int v = 1; v <= 13; v++)
                {
                    deck.Add(new Card(s, v, false));
                }
            }

            // Fisher-Yates shuffle
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = rng.Next(0, i + 1);
                Card temp = deck[i];
                deck[i] = deck[j];
                deck[j] = temp;
            }

            int deckIdx = 0;

            // Deal into 7 tableau columns
            for (int c = 0; c < 7; c++)
            {
                for (int r = 0; r <= c; r++)
                {
                    Card card = deck[deckIdx++];
                    card.IsFaceUp = (r == c); // Only top card face up
                    tableau[c].Add(card);
                }
            }

            // Rest goes to stock
            while (deckIdx < deck.Count)
            {
                Card card = deck[deckIdx++];
                card.IsFaceUp = false;
                stock.Add(card);
            }
        }

        private void CheckVictory()
        {
            for (int i = 0; i < 4; i++)
            {
                if (foundations[i].Count != 13)
                    return;
            }

            isWon = true;
            PlayTone(1046, 200);
        }

        private bool TryAutoMoveToFoundation(Card card, out int targetF)
        {
            targetF = -1;
            for (int f = 0; f < 4; f++)
            {
                if (foundations[f].Count == 0)
                {
                    if (card.Value == 1) // Ace
                    {
                        targetF = f;
                        return true;
                    }
                }
                else
                {
                    Card top = foundations[f][foundations[f].Count - 1];
                    if (top.Suit == card.Suit && top.Value + 1 == card.Value)
                    {
                        targetF = f;
                        return true;
                    }
                }
            }
            return false;
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

            if (key.Key == ConsoleKeyEx.R)
            {
                ResetGame();
                return;
            }

            if (key.Key == ConsoleKeyEx.Spacebar)
            {
                // Draw card
                DrawCardFromStock();
            }
        }

        private void DrawCardFromStock()
        {
            selSource = -1;
            selIndex = -1;

            if (stock.Count > 0)
            {
                Card card = stock[stock.Count - 1];
                stock.RemoveAt(stock.Count - 1);
                card.IsFaceUp = true;
                waste.Add(card);
                moves++;
                PlayTone(450, 15);
            }
            else if (waste.Count > 0)
            {
                // Recycle waste into stock
                for (int i = waste.Count - 1; i >= 0; i--)
                {
                    Card card = waste[i];
                    card.IsFaceUp = false;
                    stock.Add(card);
                }
                waste.Clear();
                moves++;
                PlayTone(380, 20);
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
            int contentWidth = Math.Max(1, Window.Width - 16);

            // [NOWA GRA] button in header
            int btnX = contentX + contentWidth - 84;
            int btnY = contentY + 6;
            int btnW = 76;
            int btnH = 22;

            if (leftClicked && !leftWasClicked)
            {
                if (mouseX >= btnX && mouseX < btnX + btnW &&
                    mouseY >= btnY && mouseY < btnY + btnH)
                {
                    ResetGame();
                    return;
                }
            }

            int topRowY = contentY + 38;
            int colStartX = contentX + 22;

            // 1. Stock pile click
            int stockX = colStartX;
            if (leftClicked && !leftWasClicked)
            {
                if (mouseX >= stockX && mouseX < stockX + CardW &&
                    mouseY >= topRowY && mouseY < topRowY + CardH)
                {
                    DrawCardFromStock();
                    return;
                }
            }

            // 2. Waste pile click
            int wasteX = colStartX + CardW + ColGap;
            if (waste.Count > 0 && mouseX >= wasteX && mouseX < wasteX + CardW &&
                mouseY >= topRowY && mouseY < topRowY + CardH)
            {
                if ((leftClicked && !leftWasClicked) || (rightClicked && !rightWasClicked))
                {
                    Card wasteCard = waste[waste.Count - 1];

                    // Right click or double-intent: auto-move to foundation
                    if (rightClicked && !rightWasClicked)
                    {
                        if (TryAutoMoveToFoundation(wasteCard, out int tf))
                        {
                            waste.RemoveAt(waste.Count - 1);
                            foundations[tf].Add(wasteCard);
                            score += 10;
                            moves++;
                            PlayTone(880, 25);
                            selSource = -1;
                            CheckVictory();
                            return;
                        }
                    }

                    // Left click: select waste card
                    if (leftClicked && !leftWasClicked)
                    {
                        selSource = 0;
                        selIndex = waste.Count - 1;
                        PlayTone(400, 15);
                        return;
                    }
                }
            }

            // 3. Foundations click (4 slots on top right)
            int foundStartX = colStartX + 3 * (CardW + ColGap);
            for (int f = 0; f < 4; f++)
            {
                int fx = foundStartX + f * (CardW + ColGap);
                if (mouseX >= fx && mouseX < fx + CardW &&
                    mouseY >= topRowY && mouseY < topRowY + CardH)
                {
                    if (leftClicked && !leftWasClicked)
                    {
                        if (selSource >= 0)
                        {
                            // Try placing selected single card on foundation
                            Card movingCard = GetSelectedBaseCard();
                            if (CanPlaceOnFoundation(f, movingCard))
                            {
                                MoveSelectedToFoundation(f);
                                return;
                            }
                        }
                    }
                }
            }

            // 4. Tableau columns click (7 columns)
            int tabY = contentY + 116;
            for (int c = 0; c < 7; c++)
            {
                int tx = colStartX + c * (CardW + ColGap);
                if (mouseX >= tx && mouseX < tx + CardW)
                {
                    List<Card> col = tableau[c];

                    // Click on empty column
                    if (col.Count == 0 && mouseY >= tabY && mouseY < tabY + CardH)
                    {
                        if (leftClicked && !leftWasClicked && selSource >= 0)
                        {
                            Card movingCard = GetSelectedBaseCard();
                            if (movingCard.Value == 13) // King can go to empty column
                            {
                                MoveSelectedToTableau(c);
                                return;
                            }
                        }
                    }

                    // Check click on cards in column
                    int cardCount = col.Count;
                    for (int i = cardCount - 1; i >= 0; i--)
                    {
                        int cy = GetTableauCardY(tabY, col, i);
                        int ch = (i == cardCount - 1) ? CardH : (col[i].IsFaceUp ? FaceUpStep : FaceDownStep);

                        if (mouseY >= cy && mouseY < cy + ch)
                        {
                            Card clickedCard = col[i];

                            if (!clickedCard.IsFaceUp && i == cardCount - 1)
                            {
                                // Flip top face-down card
                                if (leftClicked && !leftWasClicked)
                                {
                                    clickedCard.IsFaceUp = true;
                                    col[i] = clickedCard;
                                    score += 5;
                                    moves++;
                                    PlayTone(520, 20);
                                    selSource = -1;
                                    return;
                                }
                            }
                            else if (clickedCard.IsFaceUp)
                            {
                                // Right click: auto move to foundation
                                if (rightClicked && !rightWasClicked && i == cardCount - 1)
                                {
                                    if (TryAutoMoveToFoundation(clickedCard, out int tf))
                                    {
                                        col.RemoveAt(i);
                                        foundations[tf].Add(clickedCard);
                                        score += 10;
                                        moves++;
                                        PlayTone(880, 25);
                                        selSource = -1;
                                        RevealTopTableauCard(c);
                                        CheckVictory();
                                        return;
                                    }
                                }

                                if (leftClicked && !leftWasClicked)
                                {
                                    if (selSource >= 0)
                                    {
                                        // Try placing selected stack on this card
                                        Card movingCard = GetSelectedBaseCard();
                                        if (i == cardCount - 1 && CanPlaceOnTableau(clickedCard, movingCard))
                                        {
                                            MoveSelectedToTableau(c);
                                            return;
                                        }
                                        else
                                        {
                                            // Change selection to this card
                                            selSource = c + 1;
                                            selIndex = i;
                                            PlayTone(400, 15);
                                            return;
                                        }
                                    }
                                    else
                                    {
                                        // Select this card and all below it
                                        selSource = c + 1;
                                        selIndex = i;
                                        PlayTone(400, 15);
                                        return;
                                    }
                                }
                            }
                            break;
                        }
                    }
                }
            }
        }

        private Card GetSelectedBaseCard()
        {
            if (selSource == 0 && waste.Count > 0)
                return waste[waste.Count - 1];

            if (selSource >= 1 && selSource <= 7)
            {
                int colIdx = selSource - 1;
                if (selIndex >= 0 && selIndex < tableau[colIdx].Count)
                    return tableau[colIdx][selIndex];
            }

            return new Card(0, 0, false);
        }

        private bool CanPlaceOnFoundation(int f, Card card)
        {
            if (card.Value == 0)
                return false;

            if (foundations[f].Count == 0)
                return card.Value == 1; // Ace

            Card top = foundations[f][foundations[f].Count - 1];
            return top.Suit == card.Suit && top.Value + 1 == card.Value;
        }

        private static bool CanPlaceOnTableau(Card target, Card moving)
        {
            if (target.Value == 0 || moving.Value == 0)
                return false;

            return (target.IsRed != moving.IsRed) && (target.Value == moving.Value + 1);
        }

        private void MoveSelectedToFoundation(int f)
        {
            if (selSource == 0 && waste.Count > 0)
            {
                Card card = waste[waste.Count - 1];
                waste.RemoveAt(waste.Count - 1);
                foundations[f].Add(card);
                score += 10;
                moves++;
                PlayTone(880, 25);
            }
            else if (selSource >= 1 && selSource <= 7)
            {
                int colIdx = selSource - 1;
                // Only single card to foundation
                if (selIndex == tableau[colIdx].Count - 1)
                {
                    Card card = tableau[colIdx][selIndex];
                    tableau[colIdx].RemoveAt(selIndex);
                    foundations[f].Add(card);
                    score += 10;
                    moves++;
                    PlayTone(880, 25);
                    RevealTopTableauCard(colIdx);
                }
            }

            selSource = -1;
            selIndex = -1;
            CheckVictory();
        }

        private void MoveSelectedToTableau(int targetCol)
        {
            if (selSource == 0 && waste.Count > 0)
            {
                Card card = waste[waste.Count - 1];
                waste.RemoveAt(waste.Count - 1);
                tableau[targetCol].Add(card);
                score += 5;
                moves++;
                PlayTone(580, 20);
            }
            else if (selSource >= 1 && selSource <= 7)
            {
                int srcCol = selSource - 1;
                if (srcCol != targetCol && selIndex >= 0 && selIndex < tableau[srcCol].Count)
                {
                    int countToMove = tableau[srcCol].Count - selIndex;
                    List<Card> movingStack = new List<Card>(countToMove);
                    for (int i = selIndex; i < tableau[srcCol].Count; i++)
                    {
                        movingStack.Add(tableau[srcCol][i]);
                    }

                    tableau[srcCol].RemoveRange(selIndex, countToMove);
                    tableau[targetCol].AddRange(movingStack);

                    score += 5;
                    moves++;
                    PlayTone(580, 20);
                    RevealTopTableauCard(srcCol);
                }
            }

            selSource = -1;
            selIndex = -1;
        }

        private void RevealTopTableauCard(int colIdx)
        {
            if (tableau[colIdx].Count > 0)
            {
                Card top = tableau[colIdx][tableau[colIdx].Count - 1];
                if (!top.IsFaceUp)
                {
                    top.IsFaceUp = true;
                    tableau[colIdx][tableau[colIdx].Count - 1] = top;
                    score += 5;
                }
            }
        }

        private static int GetTableauCardY(int baseY, List<Card> col, int idx)
        {
            int y = baseY;
            for (int i = 0; i < idx; i++)
            {
                y += col[i].IsFaceUp ? FaceUpStep : FaceDownStep;
            }
            return y;
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

            // Table Background
            canvas.DrawFilledRectangle(TableBg, contentX, contentY, contentWidth, contentHeight);

            // Header panel
            canvas.DrawFilledRectangle(HeaderPanel, contentX, contentY, contentWidth, 32);
            canvas.DrawRectangle(SlotBorder, contentX, contentY, contentWidth, 32);

            SmallTextRenderer.Draw(canvas, "WYNIK", contentX + 16, contentY + 11, MutedColor);
            SmallTextRenderer.DrawUInt(canvas, score, contentX + 60, contentY + 11, TextColor);

            SmallTextRenderer.Draw(canvas, "RUCHY", contentX + 130, contentY + 11, MutedColor);
            SmallTextRenderer.DrawUInt(canvas, (ulong)moves, contentX + 175, contentY + 11, TextColor);

            // [NOWA GRA] button
            int btnX = contentX + contentWidth - 84;
            int btnY = contentY + 5;
            int btnW = 74;
            int btnH = 22;
            canvas.DrawFilledRectangle(Color.FromArgb(20, 60, 35), btnX, btnY, btnW, btnH);
            canvas.DrawRectangle(SlotBorder, btnX, btnY, btnW, btnH);
            SmallTextRenderer.DrawCentered(canvas, "NOWA GRA", btnX, btnY + 7, btnW, TextColor);

            int topRowY = contentY + 38;
            int colStartX = contentX + 22;

            // 1. Stock pile
            int stockX = colStartX;
            DrawCardSlot(canvas, stockX, topRowY);
            if (stock.Count > 0)
            {
                DrawCardBack(canvas, stockX, topRowY);
                SmallTextRenderer.DrawCentered(canvas, stock.Count.ToString(), stockX, topRowY + 26, CardW, Color.White);
            }
            else
            {
                SmallTextRenderer.DrawCentered(canvas, "PUSTO", stockX, topRowY + 26, CardW, MutedColor);
            }

            // 2. Waste pile
            int wasteX = colStartX + CardW + ColGap;
            DrawCardSlot(canvas, wasteX, topRowY);
            if (waste.Count > 0)
            {
                bool isSel = (selSource == 0);
                DrawCard(canvas, waste[waste.Count - 1], wasteX, topRowY, isSel);
            }

            // 3. Foundations (4 slots on top right)
            int foundStartX = colStartX + 3 * (CardW + ColGap);
            for (int f = 0; f < 4; f++)
            {
                int fx = foundStartX + f * (CardW + ColGap);
                DrawCardSlot(canvas, fx, topRowY);

                if (foundations[f].Count > 0)
                {
                    DrawCard(canvas, foundations[f][foundations[f].Count - 1], fx, topRowY, false);
                }
                else
                {
                    // Draw slot suit hint
                    string suitHint = f switch { 0 => "H", 1 => "D", 2 => "C", _ => "S" };
                    Color hintColor = (f < 2) ? Color.FromArgb(120, 40, 40) : Color.FromArgb(40, 80, 50);
                    SmallTextRenderer.DrawCentered(canvas, suitHint, fx, topRowY + 26, CardW, hintColor);
                }
            }

            // 4. Tableau (7 columns)
            int tabY = contentY + 116;
            for (int c = 0; c < 7; c++)
            {
                int tx = colStartX + c * (CardW + ColGap);
                List<Card> col = tableau[c];

                DrawCardSlot(canvas, tx, tabY);

                for (int i = 0; i < col.Count; i++)
                {
                    int cy = GetTableauCardY(tabY, col, i);
                    bool isSel = (selSource == c + 1 && i >= selIndex);

                    if (col[i].IsFaceUp)
                    {
                        DrawCard(canvas, col[i], tx, cy, isSel);
                    }
                    else
                    {
                        DrawCardBack(canvas, tx, cy);
                    }
                }
            }

            // Victory banner
            if (isWon)
            {
                int bw = 320;
                int bh = 50;
                int bx = contentX + (contentWidth - bw) / 2;
                int by = contentY + (contentHeight - bh) / 2;

                canvas.DrawFilledRectangle(Color.FromArgb(14, 20, 26), bx, by, bw, bh);
                canvas.DrawRectangle(SelectBorder, bx, by, bw, bh);
                SmallTextRenderer.DrawCentered(canvas, "GRATULACJE! PASJANS UKONCZONY!", bx, by + 14, bw, SelectBorder);
                SmallTextRenderer.DrawCentered(canvas, "Kliknij Nowa Gra aby zagrac ponownie.", bx, by + 30, bw, TextColor);
            }

            // Footer instructions
            SmallTextRenderer.Draw(canvas, "Klik: Zaznacz/Przenies  Prawy klik: Auto-baza  Spacja: Dobierz  R: Reset",
                contentX + 16, contentY + contentHeight - 16, MutedColor);
        }

        private static void DrawCardSlot(Canvas canvas, int x, int y)
        {
            canvas.DrawFilledRectangle(Color.FromArgb(15, 46, 26), x, y, CardW, CardH);
            canvas.DrawRectangle(SlotBorder, x, y, CardW, CardH);
        }

        private static void DrawCardBack(Canvas canvas, int x, int y)
        {
            canvas.DrawFilledRectangle(CardBackBg, x, y, CardW, CardH);
            canvas.DrawRectangle(CardBackBorder, x, y, CardW, CardH);
            canvas.DrawRectangle(Color.FromArgb(40, 90, 160), x + 3, y + 3, CardW - 6, CardH - 6);
        }

        private static void DrawCard(Canvas canvas, Card card, int x, int y, bool isSelected)
        {
            canvas.DrawFilledRectangle(CardBg, x, y, CardW, CardH);
            canvas.DrawRectangle(isSelected ? SelectBorder : CardBorder, x, y, CardW, CardH);
            if (isSelected)
            {
                canvas.DrawRectangle(SelectBorder, x + 1, y + 1, CardW - 2, CardH - 2);
            }

            Color fg = card.SuitColor;

            // Rank (top-left)
            SmallTextRenderer.Draw(canvas, card.RankString, x + 3, y + 3, fg);

            // Suit symbol (top-left below rank)
            SmallTextRenderer.Draw(canvas, card.SuitSymbol, x + 3, y + 13, fg);

            // Large center suit symbol
            SmallTextRenderer.DrawCentered(canvas, card.SuitSymbol, x, y + 26, CardW, fg);

            // Rank (bottom-right)
            int rw = SmallTextRenderer.Width(card.RankString);
            SmallTextRenderer.Draw(canvas, card.RankString, x + CardW - rw - 3, y + CardH - 12, fg);
        }
    }
}
