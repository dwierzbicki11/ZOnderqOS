#!/usr/bin/env python3
"""Source-level integration contract for the built-in Gen3 games.
Run with python3 tests/test_games_framework_gen3.py.
This does NOT replace a Cosmos compile or real QEMU boot test.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


def source(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


class GamesFrameworkGen3(unittest.TestCase):
    def test_overridable_game_callbacks(self):
        game = source("source/Games/Framework/Game.cs")
        for signature in (
            "public abstract class Game : Application",
            "protected virtual void Initialize()",
            "protected abstract void Update(float deltaSeconds)",
            "protected abstract void Render(GameCanvas graphics)",
            "protected virtual void OnKeyDown(KeyEvent key)",
            "protected virtual void OnMouseDown(int gameX, int gameY)",
            "protected virtual void Reset()",
            "Stopwatch.GetTimestamp()",
            "public sealed override void Update()",
            "public sealed override void Render(Canvas canvas)",
        ):
            self.assertIn(signature, game)

    def test_resolution_independent_canvas_and_orientation(self):
        canvas = source("source/Games/Framework/GameCanvas.cs")
        game = source("source/Games/Framework/Game.cs")
        self.assertIn("Math.Min(width / (float)logicalWidth", canvas)
        self.assertIn("TryToLogical(", canvas)
        self.assertIn("GameOrientation.Portrait", game)
        self.assertIn("Window.CloseAction = Close;", game)

    def test_examples_registered_and_redraw_is_scheduled(self):
        registry = source("source/GUI/Application/AppRegistry.cs")
        gui = source("source/GUI/GuiManager.cs")
        manager = source("source/GUI/Application/ApplicationManager.cs")
        app_center = source("source/GUI/Application/AppCenterApp.cs")
        for name in ("Pong", "Snake", "Tetris"):
            self.assertIn(f"AppIds.{name}", gui)
            self.assertIn(f'public const string {name} = "game-', registry)
        self.assertIn("AppCategory.Games", gui)
        self.assertIn('"GRY"', app_center)
        self.assertIn("HasActiveGame", manager)
        self.assertIn("applicationManager.HasActiveGame", gui)
        self.assertIn("int heartbeatFrames = applicationManager.HasActiveGame", gui)

    def test_qemu_disk_is_file_only_and_bsp(self):
        launcher = source("tools/run-game-qemu.sh")
        for token in (
            "ZonderqOS-3095-disk.iso",
            "games-data.img",
            "-smp 1",
            "-n ZONDERQDATA",
            'if [[ ! -e "$DISK" ]]',
            'bash ./run.sh --build',
        ):
            self.assertIn(token, launcher)
        self.assertNotIn("/dev/sda", launcher)
        self.assertNotIn("/dev/sdb", launcher)


if __name__ == "__main__":
    unittest.main()
