#!/usr/bin/env python3
"""Offline contract: 3.0.95 project NuGet cache survives no-op builds.

Exercise the cache-management shell fragment from the REAL builder with a
temporary local directory. Does not download SDKs or claim kernel/QEMU proof.
"""
from __future__ import annotations

from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
script = (ROOT / "tools/build-cosmos-3095-local.sh").read_text()
marker = "# The project package cache is private to this exact 3.0.95 build variant."
if script.count(marker) != 1:
    raise SystemExit("Missing/ambiguous project cache policy")
body = script.split(marker, 1)[1].split('export NUGET_PACKAGES="$PROJECT_CACHE"', 1)[0]
if "if (( rebuild )); then" not in body or 'rm -rf -- "$PROJECT_CACHE"' not in body:
    raise SystemExit("Project cache must be invalidated when Cosmos packages rebuild")
if body.count('rm -rf -- "$PROJECT_CACHE"') != 1:
    raise SystemExit("Unexpected multiple project cache deletes")
restore = script.split('dotnet restore "$STAGE/ZonderqOS.csproj"', 1)[1].split("\n\n", 1)[0]
if "--no-cache" in restore or "--force" in restore:
    raise SystemExit("Restore must not bypass reusable isolated NuGet package cache")
if 'Package provenance: PASS (upstream tag, private local feed)' not in script:
    raise SystemExit("Package origin guard was removed")

with tempfile.TemporaryDirectory(prefix="smp3095-nuget-cache-") as temp:
    root = Path(temp)
    cache = root / "isolated-project-cache"
    neighbor = root / "do-not-touch"
    neighbor.mkdir()
    (neighbor / "keep").write_text("sibling is safe")
    shell = 'set -euo pipefail\nPROJECT_CACHE="$1"\nrebuild="$2"\n' + body
    for mode in (0, 1):
        cache.mkdir(exist_ok=True)
        sentinel = cache / "preserve-if-unchanged"
        sentinel.write_text("same 3.0.95 package build")
        subprocess.run(
            ["bash", "-c", shell, "cache-test", str(cache), str(mode)],
            check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
        )
        exists = sentinel.exists()
        if mode == 0 and not exists:
            raise SystemExit("No-op build unexpectedly erased NuGet cache")
        if mode == 1 and exists:
            raise SystemExit("Package rebuild failed to invalidate NuGet cache")
        if not cache.is_dir() or not (neighbor / "keep").exists():
            raise SystemExit("Cache policy destroyed an unrelated directory")
print("[3095-NUGET-CACHE] PASS reuse when unchanged, invalidate on package rebuild")
