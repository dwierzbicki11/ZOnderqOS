#!/usr/bin/env python3
"""Stage 2: native CPU ID mapping onto exact Cosmos v3.0.95 + Stage 1.

No AP is woken and no managed threading is enabled. Derived entirely from the
pristine git tag (never blindly applies a 3.0.94 patch). Safe to rerun.
"""
from __future__ import annotations
import argparse
import importlib.util
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("smp3095_stage1", ROOT / "port-cosmos-3095-stage1.py")
if spec is None or spec.loader is None:
    raise SystemExit("[SMP3095-STAGE2][FAIL] Stage1 port module unavailable")
stage1 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(stage1)

METHOD = r'''
    /// <summary>
    /// Assign dense logical IDs: BSP is 0, APs are 1..N-1.
    /// Pre-validates the entire response before modifying ExtraArgument.
    /// GotoAddress remains NULL. This does not start application processors.
    /// </summary>
    public static bool TryAssignDenseX64CpuIds(out ulong cpuCount)
    {
        cpuCount = 0;
        if (!TryValidateX64(out ulong count, out uint bspLapicId))
        {
            return false;
        }

        LimineMpResponseX64* response = (LimineMpResponseX64*)Limine.MP.Response;
        ulong nextId = 1;
        // No partially-published map if an AP was already started.
        for (ulong i = 0; i < count; i++)
        {
            LimineMpInfoX64* desc = response->Cpus[i];
            if (desc->GotoAddress != null)
            {
                return false;
            }
        }

        for (ulong i = 0; i < count; i++)
        {
            LimineMpInfoX64* desc = response->Cpus[i];
            if (desc->LapicId == bspLapicId)
            {
                desc->ExtraArgument = 0;
            }
            else
            {
                desc->ExtraArgument = nextId++;
            }
        }

        cpuCount = count;
        return nextId == count;
    }
'''
PROOF = r'''
        // Stage 2: write only Limine's dense logical IDs; APs still parked.
        if (LimineMp.TryAssignDenseX64CpuIds(out ulong denseCount))
        {
            Serial.WriteString("[SMP3095-STAGE2] cpus=");
            Serial.WriteNumber(denseCount);
            Serial.WriteString(" dense=0..");
            Serial.WriteNumber(denseCount - 1);
            Serial.WriteString(" AP_NOT_STARTED\n");
        }
        else
        {
            Serial.WriteString("[SMP3095-STAGE2] UNAVAILABLE AP_NOT_STARTED\n");
        }
'''

def fail(message: str) -> None:
    raise SystemExit(f"[SMP3095-STAGE2][FAIL] {message}")

def run_git(root: Path, *args: str) -> str:
    result = subprocess.run(["git", "-C", str(root), *args], text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode:
        fail(f"git {' '.join(args)}: {result.stderr.strip()}")
    return result.stdout.rstrip("\n")

def expected(root: Path) -> tuple[dict[Path, str], dict[Path, str]]:
    pristine_stage1 = stage1.calculate(root)
    mp = stage1.MP
    hal = stage1.HAL
    mp_source = pristine_stage1[mp]
    anchor = "        return true;\n    }\n}\n"
    if mp_source.count(anchor) != 1:
        fail("Stage1 LimineMp.cs tail anchor changed")
    patched_mp = mp_source.replace(anchor, "        return true;\n    }\n" + METHOD + "}\n", 1)
    hal_source = pristine_stage1[hal]
    stage1_anchor = "        // Stage 1: read-only Limine topology. APs remain parked."
    if hal_source.count(stage1_anchor) != 1:
        fail("Stage1 HAL anchor changed")
    patched_hal = hal_source.replace(stage1_anchor, PROOF + "\n" + stage1_anchor, 1)
    stage2_expected = dict(pristine_stage1)
    stage2_expected[mp] = patched_mp
    stage2_expected[hal] = patched_hal
    return pristine_stage1, stage2_expected

def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("cosmos_checkout", type=Path)
    p.add_argument("--apply", action="store_true")
    a = p.parse_args()
    root = a.cosmos_checkout.resolve()
    if not (root / ".git").is_dir():
        fail("expected isolated Cosmos .git directory")
    if run_git(root, "rev-parse", "HEAD") != run_git(root, "rev-parse", "refs/tags/v3.0.95^{commit}"):
        fail("checkout must be pinned to exact Cosmos v3.0.95")
    base, dest = expected(root)
    tracked = [stage1.BOOT, stage1.HAL]
    all_paths = [stage1.BOOT, stage1.HAL, stage1.MP]
    status = set(run_git(root, "status", "--porcelain", "--untracked-files=all").splitlines())
    allowed_status = {f" M {stage1.BOOT}", f" M {stage1.HAL}", f"?? {stage1.MP}"}
    base_status = not status and not (root / stage1.MP).exists()
    staged1 = all((root / k).is_file() and (root / k).read_text() == v
                  for k, v in base.items()) and status == allowed_status
    staged2 = all((root / k).is_file() and (root / k).read_text() == v
                  for k, v in dest.items()) and status == allowed_status
    if not (base_status or staged1 or staged2):
        fail("unrecognized partial patch or unrelated Cosmos worktree changes")
    if a.apply and (base_status or staged1):
        for path, text in dest.items():
            if path not in tracked and (root / path).exists() and not staged1:
                fail("refusing to overwrite existing new source")
            (root / path).write_text(text)
        print("[SMP3095-STAGE2] PATCHED exact 3.0.95 source; native APs remain parked")
    elif staged2:
        print("[SMP3095-STAGE2] REUSED verified exact overlay")
    else:
        print("[SMP3095-STAGE2] CHECK PASS: safe topology overlay can be applied")
    print("[SMP3095-STAGE2] NOT an AP-start or managed SMP proof")

if __name__ == "__main__":
    main()
