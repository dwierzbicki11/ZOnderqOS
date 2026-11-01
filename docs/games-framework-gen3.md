# ZonderqOS Gen3 — gry w C# (Cosmos 3.0.95)

Ten branch dodaje szkielet gier kompilowanych **razem z systemem**.
Nie jest to jeszcze loader zewnętrznych EXE/DLL. Kod trafia do projektu
`ZonderqOS.csproj` przez standardowe włączanie plików C# przez SDK.

## Struktura
- `source/Games/Framework/Game.cs` — klasa bazowa: okno, pętla aktualizacji, pauza, reset i wejście.
- `source/Games/Framework/GameCanvas.cs` — współrzędne logiczne, skalowanie do okna, letterboxing.
- `source/Games/Pong/PongGame.cs` — grywalny przykład z CPU.
- `source/Games/Snake/SnakeGame.cs` — pionowy szkielet z kursorem; brak ogona/jedzenia/kolizji.
- `source/Games/Tetris/TetrisGame.cs` — pionowy szkielet planszy; brak mechaniki klocków.

Gry są widoczne w App Center (kategoria GRY) i menu Start.

## Jak stworzyć nową grę

Utwórz `source/Games/MojaGra/MojaGra.cs`:

```csharp
using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.Games.Framework;

namespace ZonderqOS.Games.MojaGra
{
    public sealed class MojaGra : Game
    {
        private int x;

        public MojaGra(int windowX, int windowY)
            : base("Moja Gra", windowX, windowY, 640, 360,
                GameOrientation.Landscape, 60)
        {
        }

        protected override void Initialize() { x = 20; }

        protected override void Update(float deltaSeconds)
        {
            x += (int)(100 * deltaSeconds + 1);
            if (x > Width - 20) x = 0;
        }

        protected override void Render(GameCanvas graphics)
        {
            graphics.Clear(Color.Black);
            graphics.FillRect(Color.Cyan, x, 80, 20, 20);
            graphics.DrawText("MOJA GRA", 12, 12, Color.White);
        }

        protected override void OnKeyDown(KeyEvent key)
        {
            if (key.Key == ConsoleKeyEx.LeftArrow) x -= 10;
            if (key.Key == ConsoleKeyEx.RightArrow) x += 10;
        }
    }
}
```

Następnie w `AppRegistry.AppIds` dodaj unikatowy identyfikator aplikacji.
W `GuiManager.InitializeAppRegistry()` zarejestruj `AppDescriptor` z
`AppCategory.Games`, delegatem `() => applicationManager.Launch(new MojaGra(90, 70))`,
pozycją `StartMenuPlacement.Tool` i `showInAppCenter: true`. Zobacz wpis Pong.

### Życie gry
- `Initialize()` — uruchamiane po zakończeniu konstruktora gry.
- `Update(float deltaSeconds)` — stały krok czasu, maks. 5 kroków nadrabiania.
- `Render(GameCanvas)` — współrzędne logiczne, renderowanie bez pętli blokującej.
- `OnKeyDown(KeyEvent)` — zdarzenia klawiszy, nie gwarantuje ciągłego stanu trzymania.
- `OnMouseDown(int x, int y)` — kliknięcie przeliczone na współrzędne gry.
- `Reset()` — domyślnie ponownie wywołuje `Initialize()`, można nadpisać.

Pauza: Spacja. Reset: R. Wyjście: Esc. Gra działa tylko gdy okno jest aktywne.
Przy mniejszym ekranie zawartość jest proporcjonalnie skalowana, bez rozciągania.

## Kompilacja i testy w QEMU

Pracuj na branchu `feat/games-framework-gen3`. Zacommituj pliki, ponieważ
`tools/build-cosmos-3095-local.sh` buduje z `git archive HEAD` i
odmawia uruchomienia z niezapisanymi zmianami śledzonymi przez Git.

```bash
git add source/Games source/GUI docs/games-framework-gen3.md tools/run-game-qemu.sh
git commit -m "games: implement my game"
bash tools/run-game-qemu.sh --build
```

Kolejny start bez przebudowy:
```bash
bash tools/run-game-qemu.sh
```

Wymagane narzędzia QEMU i FAT: `qemu-system-x86_64`, `sfdisk`, `mkfs.vfat`.
Na Mint/Kali: `sudo apt install qemu-system-x86 fdisk dosfstools`.

Skrypt tworzy tylko plik `output-3095/qemu/games-data.img` i używa
osobnej partycji FAT32 `ZONDERQDATA`. Nigdy nie formatuje hostowego
/dev/sdX. QEMU działa w BSP-only (1 vCPU), zgodnie z Cosmos v3.0.95.

## Granice weryfikacji
Implementacja nie jest potwierdzonym testem w QEMU ani realnym sprzęcie,
dopóki pełny build ISO i rozgrywka nie zakończą się sukcesem.
