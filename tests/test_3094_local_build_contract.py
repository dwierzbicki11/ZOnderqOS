#!/usr/bin/env python3
"""Fail-closed local Cosmos 3.0.94 source/package provenance contract."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = (ROOT / "tools/build-cosmos-3094-local.sh").read_text()
LAUNCHER = (ROOT / "run-3094.sh").read_text()


class Local3094BuildContract(unittest.TestCase):
    def test_exact_upstream_tag(self):
        for item in ('VERSION="3.0.94"', 'v$VERSION^{commit}',
                     'head_sha', 'tag_sha',
                     'VersionPrefix="$VERSION"'):
            self.assertIn(item, SCRIPT)

    def test_isolated_packages_and_unchanged_checkout(self):
        for item in ('ISOLATED_HOME=', 'PROJECT_CACHE=', 'SOURCE_CACHE=',
                     'git -C "$ROOT" archive HEAD',
                     'git -C "$ROOT" status --porcelain',
                     'NUGET_PACKAGES="$PROJECT_CACHE"',
                     'cat > "$STAGE/NuGet.Config"'):
            self.assertIn(item, SCRIPT)
        self.assertNotIn('cat > "$ROOT/NuGet.Config"', SCRIPT)

    def test_no_public_cosmos_packages(self):
        for item in ('<package pattern="Cosmos.*" />', 
                     'json.loads(metadata.read_text())["source"]',
                     'if source != feed:', 'required - found'):
            self.assertIn(item, SCRIPT)

    def test_no_disk_writes_or_faked_smp(self):
        for forbidden in ("mkfs.", "wipefs", "dd if=", "parted ", 
                          'git reset --hard', "3.0.85"):
            self.assertNotIn(forbidden, SCRIPT)
        self.assertIn('ZonderqRamRoot=false', SCRIPT)
        self.assertIn('ZONDERQDATA', SCRIPT)

    def test_launcher_routes_correct_builds(self):
        self.assertIn('--build)', LAUNCHER)
        self.assertIn('--build-probe)', LAUNCHER)
        self.assertIn('build-cosmos-3094-local.sh" disk', LAUNCHER)
        self.assertIn('build-cosmos-3094-local.sh" probe', LAUNCHER)


if __name__ == "__main__":
    unittest.main()
