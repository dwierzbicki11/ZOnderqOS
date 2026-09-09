using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Mouse;
using ZonderqOS.GUI.Apps;

namespace ZonderqOS.GUI
{
    public class GuiManager
    {
        private Canvas canvas;
        private Taskbar taskbar;
        private StartMenu startMenu;
        
        // Zamiast sztywnych okien - dynamiczna lista aplikacji
        private List<Window> openWindows = new List<Window>();
        
        private bool isRunning = true;

        public void Run()
        {
            try
            {
                canvas = Canvas.GetFullScreen();
                MouseManager.SetScreenSize(canvas.Width, canvas.Height);
                int taskbarHeight = 30;

                // ==========================================
                // 1. INICJALIZACJA MENU START I PASKA
                // ==========================================
                int menuWidth = 200;
                int menuHeight = 160;
                startMenu = new StartMenu(0, (int)canvas.Height - taskbarHeight - menuHeight, menuWidth, menuHeight);
                
                // Tworzenie osobnego okna aplikacji po kliknięciu
                startMenu.AddItem("Terminal CLI", () => {
                    TerminalApp newTerminal = null;
                    
                    // Kaskadowe przesunięcie (jeśli mamy już inne okna, otwieramy lekko obok)
                    int offset = openWindows.Count * 25;
                    
                    newTerminal = new TerminalApp(100 + offset, 80 + offset, () => {
                        openWindows.Remove(newTerminal); // Usuń z listy otwartych aplikacji
                    });
                    
                    openWindows.Add(newTerminal);
                });
                
                startMenu.AddItem("Diagnostyka", () => Console.WriteLine("[ZonderqOS] Diagnostyka sprzętu..."));
                startMenu.AddItem("O Systemie", () => Console.WriteLine("[ZonderqOS] v0.3 Cosmos Gen3"));
                startMenu.AddItem("Wyjdź z GUI", () => isRunning = false);

                taskbar = new Taskbar((int)canvas.Width, (int)canvas.Height, taskbarHeight, () => {
                    startMenu.Visible = !startMenu.Visible;
                });

                bool previousLeftButtonState = false;

                // ==========================================
                // 2. GŁÓWNA PĘTLA
                // ==========================================
                while (isRunning)
                {
                    // --- OBSŁUGA KLAWIATURY ---
                    while (KeyboardManager.TryReadKey(out KeyEvent? key))
                    {
                        if (key != null)
                        {
                            if (key.Key == ConsoleKeyEx.Escape)
                                isRunning = false;
                                
                            // Routing klawiatury: wyślij klawisz do aplikacji znajdującej się NA SAMEJ GÓRZE stosu (ostatnia na liście)
                            if (openWindows.Count > 0)
                            {
                                var activeWindow = openWindows[openWindows.Count - 1];
                                if (activeWindow is TerminalApp termApp)
                                {
                                    termApp.HandleKeyboard(key);
                                }
                            }
                        }
                    }

                    // --- POBIERANIE STANU MYSZY ---
                    int mouseX = (int)MouseManager.X;
                    int mouseY = (int)MouseManager.Y;
                    bool currentLeftButtonState = MouseManager.LeftButton;

                    // --- LOGIKA KLIKNIĘĆ DLA PASKA I MENU ---
                    startMenu.UpdateInteractions(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState);
                    taskbar.UpdateInteractions(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState);

                    // --- LOGIKA KLIKNIĘĆ DLA ZARZĄDZANIA OKNAMI ---
                    // Odwrócona pętla (for-loop wstecz) zapobiega błędom modyfikacji kolekcji podczas zamykania
                    for (int i = openWindows.Count - 1; i >= 0; i--)
                    {
                        var win = openWindows[i];
                        foreach (var child in win.Children)
                        {
                            if (child is Button btn)
                            {
                                bool isOver = btn.Contains(mouseX, mouseY);
                                btn.IsHovered = isOver;

                                if (isOver && currentLeftButtonState && !previousLeftButtonState)
                                {
                                    btn.InvokeClick();
                                }
                            }
                            else if (child is TerminalBox tBox)
                            {
                                if (currentLeftButtonState && !previousLeftButtonState)
                                {
                                    tBox.IsFocused = tBox.Contains(mouseX, mouseY);
                                }
                            }
                        }
                    }

                    // Zamykanie menu po utracie focusu
                    if (currentLeftButtonState && !previousLeftButtonState && startMenu.Visible)
                    {
                        bool isOverMenu = mouseX >= startMenu.X && mouseX <= startMenu.X + startMenu.Width && 
                                          mouseY >= startMenu.Y && mouseY <= startMenu.Y + startMenu.Height;
                        bool isOverTaskbar = mouseY >= canvas.Height - taskbarHeight;

                        if (!isOverMenu && !isOverTaskbar)
                        {
                            startMenu.Visible = false;
                        }
                    }

                    previousLeftButtonState = currentLeftButtonState;

                    // --- RENDEROWANIE SCENY ---
                    canvas.Clear(Color.MidnightBlue);

                    // Rysowanie wszystkich otwartych okien (kolejność od najstarszego do najnowszego - zachowanie Z-Indexu)
                    foreach (var win in openWindows)
                    {
                        win.Render(canvas);
                    }

                    taskbar.Render(canvas);
                    startMenu.Render(canvas);
                    Cursor.Draw(canvas, mouseX, mouseY);

                    canvas.Display();
                    Thread.Sleep(15);
                }

                canvas.Clear(Color.Black);
                canvas.Display();
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Błąd w pętli GUI: {ex.Message}", "GUI");
            }
        }
    }
}