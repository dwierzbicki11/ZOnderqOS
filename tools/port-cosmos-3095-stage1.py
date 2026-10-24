#!/usr/bin/env python3
"""Strict, opt-in Limine MP discovery port for EXACT Cosmos v3.0.95.

Changes only the isolated upstream checkout. This stage does NOT wake APs,
enable managed SMP, or touch ZonderqOS / physical storage.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import subprocess

TAG = "v3.0.95"
BOOT = Path("src/Cosmos.Kernel.Boot.Limine/Limine.cs")
HAL = Path("src/Cosmos.Kernel.HAL.X64/X64PlatformInitializer.cs")
MP = Path("src/Cosmos.Kernel.Boot.Limine/LimineMp.cs")

MP_SOURCE = r'''using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Boot.Limine;

/// <summary>Limine x86_64 MP request, xAPIC addressing (Flags = 0).</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct LimineMpRequest()
{
    public readonly LimineID ID = new(0x95a67b819a1b857e, 0xa0b61b723b6a73e0);
    public readonly ulong Revision = 0;
    public readonly void* Response;
    public readonly ulong Flags = 0;
}

[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct LimineMpResponseX64
{
    public readonly ulong Revision;
    public readonly uint Flags;
    public readonly uint BspLapicId;
    public readonly ulong CpuCount;
    public readonly LimineMpInfoX64** Cpus;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct LimineMpInfoX64
{
    public uint ProcessorId;
    public uint LapicId;
    public ulong Reserved;
    public delegate* unmanaged<LimineMpInfoX64*, void> GotoAddress;
    public ulong ExtraArgument;
}

/// <summary>
/// Validates Limine's MP response before APs are activated.
/// This is *discovery only*: no GotoAddress writes or AP startup.
/// </summary>
public static unsafe class LimineMp
{
    public const ulong MaxXApicCpuCount = 256;

    public static bool TryValidateX64(out ulong cpuCount, out uint bspLapicId)
    {
        cpuCount = 0;
        bspLapicId = 0;
        LimineMpResponseX64* response = (LimineMpResponseX64*)Limine.MP.Response;
        if (response == null || response->Cpus == null ||
            response->CpuCount == 0 || response->CpuCount > MaxXApicCpuCount ||
            (response->Flags & 1u) != 0)
        {
            return false;
        }

        ulong bspCount = 0;
        for (ulong i = 0; i < response->CpuCount; i++)
        {
            LimineMpInfoX64* cpu = response->Cpus[i];
            if (cpu == null || cpu->LapicId >= MaxXApicCpuCount)
                return false;

            if (cpu->LapicId == response->BspLapicId)
                bspCount++;

            for (ulong j = 0; j < i; j++)
            {
                LimineMpInfoX64* previous = response->Cpus[j];
                if (previous == null || previous->LapicId == cpu->LapicId)
                    return false;
            }
        }

        if (bspCount != 1)
            return false;

        cpuCount = response->CpuCount;
        bspLapicId = response->BspLapicId;
        return true;
    }
}
'''

def fail(message: str) -> None:
    raise SystemExit(f"[SMP3095-STAGE1][FAIL] {message}")

def git(root: Path, *args: str) -> str:
    p = subprocess.run(["git", "-C", str(root), *args],
                       text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if p.returncode:
        fail(f"git {' '.join(args)}: {p.stderr.strip()}")
    return p.stdout.strip()

def replace_exact(text: str, original: str, replacement: str, name: str) -> str:
    if text.count(original) != 1:
        fail(f"{name}: expected one exact v3.0.95 anchor; found {text.count(original)}")
    return text.replace(original, replacement, 1)

def calculate(root: Path) -> dict[Path, str]:
    if (root / MP).exists():
        fail("LimineMp.cs already exists; refuse partial or repeated patch")
    try:
        boot = (root / BOOT).read_text()
        hal = (root / HAL).read_text()
    except OSError as ex:
        fail(f"source file missing: {ex}")

    anchor = "    public static readonly LimineStackSizeRequest StackSize = new(LimineStackSizeRequest.DefaultRequestedSize);"
    boot = replace_exact(boot, anchor,
        "    public static readonly LimineMpRequest MP = new();\n" + anchor, str(BOOT))
    hal = replace_exact(hal, "using Cosmos.Kernel.Core;\n",
        "using Cosmos.Kernel.Boot.Limine;\nusing Cosmos.Kernel.Core;\n", str(HAL))
    anchor = "        AcpiMadt.DisplayMadtInfo();"
    proof = '''
        // SMP Stage 1: read-only Limine topology. APs remain parked.
        if (LimineMp.TryValidateX64(out ulong count, out uint bsp))
        {
            Serial.WriteString("[SMP3095-STAGE1] Limine MP cpus=");
            Serial.WriteNumber(count);
            Serial.WriteString(" bsp_apic=");
            Serial.WriteNumber(bsp);
            Serial.WriteString(" AP_NOT_STARTED\\\\n");
        }
        else
        {
            Serial.WriteString("[SMP3095-STAGE1] UNAVAILABLE AP_NOT_STARTED\\\\n");
        }
'''
    # Generate actual C# \n escape, not a literal newline in a string constant.
    proof = proof.replace('\\\\n"', '\\n"') if False else proof.replace('\\\\\\\\n', '\\\\n')
    hal = replace_exact(hal, anchor, anchor + "\n" + proof, str(HAL))
    return {BOOT: boot, HAL: hal, MP: MP_SOURCE}

def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("cosmos_checkout", type=Path)
    p.add_argument("--apply", action="store_true",
                   help="otherwise perform a read-only compatibility check")
    a = p.parse_args()
    root = a.cosmos_checkout.resolve()
    if not (root / ".git").exists():
        fail("expected an isolated Cosmos git checkout")
    tagsha = git(root, "rev-parse", f"refs/tags/{TAG}^{{commit}}")
    head = git(root, "rev-parse", "HEAD")
    if head != tagsha:
        fail(f"HEAD is not exact {TAG}")
    if git(root, "status", "--porcelain", "--untracked-files=all"):
        fail("upstream checkout must be completely clean")
    edits = calculate(root)
    if a.apply:
        for name, data in edits.items():
            (root / name).write_text(data)
        print("[SMP3095-STAGE1] PATCHED exact Cosmos v3.0.95 isolated checkout")
    else:
        print("[SMP3095-STAGE1] CHECK PASS upstream anchors; no files modified")
    print("[SMP3095-STAGE1] NOT a managed SMP or AP-start runtime proof")

if __name__ == "__main__":
    main()
