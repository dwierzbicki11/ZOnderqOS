#!/usr/bin/env python3
"""Cursor damage-rectangle safety contract for Cosmos Kernel 3.0.94.

This checks code structure and simulates overlapping saved-under cursor moves.
Only the full 3.0.94 workflow proves compilability and QEMU boot.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
source = lambda p: (ROOT / p).read_text(encoding="utf-8")
CURSOR = source("source/GUI/CursorDamageRenderer.cs")
DESKTOP = source("source/GUI/GuiManager.cs")
PROJECT = source("ZonderqOS.csproj")

class CursorDamage3094(unittest.TestCase):
    def test_pinned_cosmos_and_single_small_underlay(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.94"', PROJECT)
        self.assertIn('Version="3.0.94"', PROJECT)
        self.assertIn('DamageWidth = 24', CURSOR)
        self.assertIn('DamageHeight = 28', CURSOR)
        self.assertIn("new Color[DamageWidth * DamageHeight]", CURSOR)
        self.assertNotIn("new Canvas(", CURSOR)
        self.assertNotIn("GetImage(", CURSOR)
        self.assertNotIn("new Bitmap(", CURSOR)

    def test_cursor_restore_before_capture(self):
        move = CURSOR.split("internal bool TryMove(", 1)[1].split("private void Capture(", 1)[0]
        self.assertLess(move.index("Restore(previousX, previousY);"),
                        move.index("Capture(x, y);"))
        self.assertLess(move.index("Capture(x, y);"),
                        move.index("Cursor.Draw(canvas, x, y);"))
        self.assertLess(move.index("Cursor.Draw(canvas, x, y);"),
                        move.index("canvas.Display();"))
        self.assertIn("canvas.GetPointColor(px, py)", CURSOR)
        self.assertIn("canvas.DrawPoint(savedPixels[offset + col], px, py)", CURSOR)

    def test_no_cursor_fast_path_during_ui_damage(self):
        self.assertIn("cursorDamageRenderer?.Reset();", DESKTOP)
        self.assertIn("cursorDamageRenderer.DrawAfterScene(mouseX, mouseY)", DESKTOP)
        self.assertIn("!keyboardActivity && !buttonChanged", DESKTOP)
        self.assertIn("!currentLeftButtonState && !currentRightButtonState", DESKTOP)
        self.assertIn("!heartbeat && !UiAnimator.NeedsRedraw", DESKTOP)
        self.assertIn("sceneHasToast", DESKTOP)
        self.assertIn("NotificationService.TryGetActiveToast(out toast)", DESKTOP)
        self.assertIn("applicationManager.HasLiveTelemetryWindow", DESKTOP)
        self.assertIn("IsQuietCursorPosition(oldX, oldY) && IsQuietCursorPosition(x, y)", DESKTOP)
        self.assertIn("shortcut.Width, shortcut.Height, margin", DESKTOP)
        self.assertIn("window.X, window.Y, window.Width, window.Height, margin", DESKTOP)

    def test_old_position_is_preserved(self):
        self.assertLess(DESKTOP.index("int priorMouseX = previousMouseX;"),
                        DESKTOP.index("previousMouseX = mouseX;", DESKTOP.index("int priorMouseX = previousMouseX;")))
        self.assertIn("CanMoveCursorWithoutSceneRedraw(priorMouseX, priorMouseY, mouseX, mouseY)", DESKTOP)
        self.assertNotIn("CanMoveCursorWithoutSceneRedraw(previousMouseX, previousMouseY, mouseX, mouseY)", DESKTOP)

    def test_overlap_and_edges_keep_original_scene_pixels(self):
        w, h = 48, 36
        scene = [[(x * 131 + y * 37) % 997 for x in range(w)] for y in range(h)]
        working = [line[:] for line in scene]
        dims = (24, 28)
        halo = (4, 4)

        def snapshot(x, y):
            return [
                scene_pixel(x + dx - halo[0], y + dy - halo[1])
                for dy in range(dims[1]) for dx in range(dims[0])
            ]

        def scene_pixel(x, y):
            return working[y][x] if 0 <= x < w and 0 <= y < h else None

        def paint(x, y):
            # Synthetic cursor: 16x20 box of opaque pixels.
            for dy in range(20):
                for dx in range(16):
                    px, py = x + dx, y + dy
                    if 0 <= px < w and 0 <= py < h:
                        working[py][px] = -1

        def restore(x, y, saved):
            for dy in range(dims[1]):
                for dx in range(dims[0]):
                    px, py = x + dx - halo[0], y + dy - halo[1]
                    if 0 <= px < w and 0 <= py < h:
                        working[py][px] = saved[dy * dims[0] + dx]

        old = (7, 7)
        saved = snapshot(*old)
        paint(*old)
        for new in ((8, 8), (1, 0), (43, 32), (24, 18), (6, 12)):
            restore(*old, saved)
            self.assertEqual(working, scene)  # No ghost behind old cursor.
            saved = snapshot(*new)
            old = new
            paint(*new)
        restore(*old, saved)
        self.assertEqual(working, scene)


if __name__ == "__main__":
    unittest.main(verbosity=2)
