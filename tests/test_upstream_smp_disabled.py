#!/usr/bin/env python3
"""Static guard: no local SMP/SMT code in the active project tree."""
from pathlib import Path
import re
ROOT = Path(__file__).resolve().parents[1]
obsolete = (
    "patches/cosmos-smt", "patches/cosmos-3094",
    "tools/prepare-cosmos-smt.sh", "tools/verify-smt-local.sh",
    "run-3094.sh", "run-core.sh",
    "source/Core/HtopSmokeTest.cs",
    ".github/workflows/smt-stage22.yml",
    ".github/workflows/smt-stage23.yml",
)
for entry in obsolete:
    assert not (ROOT / entry).exists(), f"retired SMP code still active: {entry}"
project = (ROOT / "ZonderqOS.csproj").read_text()
launcher = (ROOT / "run.sh").read_text()
builder = (ROOT / "tools/build-cosmos-3095-local.sh").read_text()
assert 'Cosmos.Sdk/3.0.95' in project
assert 'Version="3.0.95"' in project
assert "<CosmosEnableScheduler>true</CosmosEnableScheduler>" in project
assert re.search(r"(?<!\w)-smp\s+1(?!\w)", launcher), "QEMU needs exactly 1 vCPU"
assert 'build-cosmos-3095-local.sh' in launcher
assert 'prepare-cosmos-smt.sh' not in launcher
cmd = (ROOT / "source/Shell/Commands/CmdSmtCheck.cs").read_text()
assert "[SMTCHECK] DISABLED" in cmd
assert "PASS: scheduler" not in cmd
assert 'VERSION="3.0.95"' in builder
assert 'ZONDERQ_3095_NATIVE_STAGE4' in builder, "stage4 must be rejected"
assert "--force --no-cache" not in builder, "NuGet cache must be reusable"
print("[SMP-PAUSED] PASS: stock upstream runtime, no patched SMP, QEMU 1 vCPU")
