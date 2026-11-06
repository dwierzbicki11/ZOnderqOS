"""Regression contract: storage sidebar never publishes partial probe results."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "source/GUI/Application/FileManagerApp.cs"


class StorageSidebarAtomicity3095(unittest.TestCase):
    def test_volume_and_device_rows_are_staged_before_publication(self):
        source = SOURCE.read_text(encoding="utf-8")
        start = source.index("private void RebuildSidebar()")
        end = source.index("private static string FormatCapacity", start)
        body = source[start:end]
        self.assertIn("List<SidebarEntry> volumeRows = new List<SidebarEntry>();", body)
        self.assertIn("List<SidebarEntry> deviceRows = new List<SidebarEntry>();", body)
        self.assertIn("volumeRows.Clear();", body)
        self.assertIn("deviceRows.Clear();", body)
        self.assertIn('volumeRows.Add(SidebarEntry.Device("Volumes"', body)
        self.assertIn('deviceRows.Add(SidebarEntry.Device("Storage"', body)
        self.assertIn("sidebarEntries.AddRange(volumeRows);", body)
        self.assertIn("sidebarEntries.AddRange(deviceRows);", body)
        self.assertLess(body.index("volumeRows.Clear();"), body.index("sidebarEntries.AddRange(volumeRows);"))
        self.assertLess(body.index("deviceRows.Clear();"), body.index("sidebarEntries.AddRange(deviceRows);"))
        self.assertNotIn("sidebarEntries.Add(SidebarEntry.Volume(", body)
        self.assertNotIn('sidebarEntries.Add(SidebarEntry.Device("Disk "', body)


if __name__ == "__main__":
    unittest.main()
