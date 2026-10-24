#!/usr/bin/env python3
"""Stage 3: BSP GS identity and RESERVED AP stacks on exact Cosmos v3.0.95.

This stage does NOT publish Limine AP entry pointers or execute managed code
on APs. It is a strictly checked overlay on the Stage2 source composition.
"""
from __future__ import annotations

import argparse
import importlib.util
from pathlib import Path
import subprocess

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("smp3095_stage2", HERE / "port-cosmos-3095-stage2.py")
if spec is None or spec.loader is None:
    raise SystemExit("[SMP3095-STAGE3][FAIL] Stage2 script missing")
stage2 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(stage2)
stage1 = stage2.stage1

CPU_LOCAL = Path("src/Cosmos.Kernel.Core/CPU/CpuLocal.cs")
SCHEDULER = Path("src/Cosmos.Kernel.Core/Scheduler/SchedulerManager.cs")
HAL_INIT = Path("src/Cosmos.Kernel.HAL/Internal/Runtime/CompilerHelpers/LibraryInitializer.cs")
ASM = Path("src/Cosmos.Kernel.Native.X64/CPU/Interrupts.s")

CPU_LOCAL_CODE = r'''using System.Runtime.InteropServices;
using System.Threading;

namespace Cosmos.Kernel.Core.CPU;

/// <summary>Stage3 BSP-local x64 identity; native APs remain parked by Limine.</summary>
internal static partial class CpuLocal
{
#if ARCH_X64
    private static int s_initialized;

    [LibraryImport("*", EntryPoint = "_native_x64_init_percpu")]
    [SuppressGCTransition]
    private static partial void NativeInitialize(uint cpuId);

    [LibraryImport("*", EntryPoint = "_native_x64_get_current_cpu_id")]
    [SuppressGCTransition]
    private static partial uint NativeGetCurrentCpuId();

    [LibraryImport("*", EntryPoint = "_native_x64_smp3095_ap_stack_top")]
    [SuppressGCTransition]
    private static partial ulong NativeApStackTop(uint cpuId);

    internal static bool InitializeBsp()
    {
        NativeInitialize(0);
        if (NativeGetCurrentCpuId() != 0)
        {
            return false;
        }

        Volatile.Write(ref s_initialized, 1);
        return true;
    }

    internal static uint CurrentCpuId =>
        Volatile.Read(ref s_initialized) == 0 ? 0 : NativeGetCurrentCpuId();

    internal static bool ValidateApStacks(uint cpuCount)
    {
        if (cpuCount == 0 || cpuCount > 256)
        {
            return false;
        }

        ulong previousTop = 0;
        for (uint cpuId = 1; cpuId < cpuCount; cpuId++)
        {
            ulong top = NativeApStackTop(cpuId);
            if (top == 0 || (top & 4095UL) != 0 ||
                (previousTop != 0 && top - previousTop != 16384UL))
            {
                return false;
            }

            previousTop = top;
        }

        return true;
    }
#else
    internal static bool InitializeBsp() => true;
    internal static uint CurrentCpuId => 0;
    internal static bool ValidateApStacks(uint cpuCount) => cpuCount > 0;
#endif
}
'''

ASM_NATIVE = r'''// Stage 3: GS-relative logical CPU ID; BSP only until native AP bootstrap.
.global _native_x64_init_percpu
_native_x64_init_percpu:
    cmp edi, 255
    ja .Lsmp3095_bad_id
    lea r8, [rip + _smp3095_cpu_slots]
    mov eax, edi
    shl rax, 6
    add r8, rax
    mov qword ptr [r8], rdi
    mov rax, r8
    mov rdx, rax
    shr rdx, 32
    mov ecx, 0xC0000101
    wrmsr
    ret
.Lsmp3095_bad_id:
    ud2

.global _native_x64_get_current_cpu_id
_native_x64_get_current_cpu_id:
    mov eax, dword ptr gs:[0]
    ret

// A reserved but UNUSED AP stack at stage3; BSP and invalid IDs -> zero.
.global _native_x64_smp3095_ap_stack_top
_native_x64_smp3095_ap_stack_top:
    test edi, edi
    jz .Lsmp3095_bad_stack
    cmp edi, 255
    ja .Lsmp3095_bad_stack
    lea rax, [rip + _smp3095_ap_stacks]
    mov ecx, edi
    shl rcx, 14
    add rax, rcx
    add rax, 16384
    ret
.Lsmp3095_bad_stack:
    xor eax, eax
    ret

'''
ASM_BSS = r'''// Stage3 storage: cache-line CPU slots and 256 reserved 16 KiB AP stacks.
.balign 64
_smp3095_cpu_slots: .zero 16384
.balign 4096
_smp3095_ap_stacks: .zero 4194304
'''

INIT_OLD = r'''        if (InterruptManager.IsEnabled)
        {
            Serial.WriteString("[KERNEL]   - Initializing interrupts...\n");'''
INIT_NEW = r'''        if (InterruptManager.IsEnabled)
        {
            // Stage3: set BSP GS base before hardware IRQ initialization.
            if (!CpuLocal.InitializeBsp())
            {
                Panic.Halt("SMP3095 Stage3: BSP per-CPU identity failed.");
            }

            Serial.WriteString("[KERNEL]   - Initializing interrupts...\n");'''

HAL_PROOF = r'''
        // AP stacks are reserved, but must not be used before Stage4.
        if (LimineMp.TryValidateX64(out ulong gsCount, out _) &&
            CpuLocal.CurrentCpuId == 0 &&
            CpuLocal.ValidateApStacks((uint)gsCount))
        {
            Serial.WriteString("[SMP3095-STAGE3] BSP_GS=0 cpus=");
            Serial.WriteNumber(gsCount);
            Serial.WriteString(" AP_STACKS_RESERVED AP_NOT_STARTED\n");
        }
        else
        {
            Serial.WriteString("[SMP3095-STAGE3] FAIL AP_NOT_STARTED\n");
            Cosmos.Kernel.Core.Panic.Halt("SMP3095 Stage3 per-CPU or AP stack validation failed.");
        }
'''

def fail(msg: str) -> None:
    raise SystemExit(f"[SMP3095-STAGE3][FAIL] {msg}")

def run_git(root: Path, *args: str) -> str:
    p = subprocess.run(["git", "-C", str(root), *args],
                       text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if p.returncode:
        fail(f"git {' '.join(args)}: {p.stderr.strip()}")
    return p.stdout.rstrip("\n")

def calculate(root: Path) -> tuple[dict[Path, str], dict[Path, str]]:
    _, stage2_source = stage2.expected(root)
    scheduler = stage1.source_at_head(root, SCHEDULER)
    hal_init = stage1.source_at_head(root, HAL_INIT)
    asm = stage1.source_at_head(root, ASM)
    scheduler = stage1.replace_exact(scheduler,
        "    internal static uint GetCurrentCpuId() => 0;",
        "    internal static uint GetCurrentCpuId() => CpuLocal.CurrentCpuId;", str(SCHEDULER))
    hal_init = stage1.replace_exact(hal_init, INIT_OLD, INIT_NEW, str(HAL_INIT))
    asm = stage1.replace_exact(asm,
        "// void _native_set_context_switch_sp",
        ASM_NATIVE + "// void _native_set_context_switch_sp", str(ASM))
    asm = stage1.replace_exact(asm,
        ".bss\n",
        ".bss\n" + ASM_BSS, str(ASM))
    hal = stage2_source[stage1.HAL]
    hal = stage1.replace_exact(hal,
        "        // Stage 2: write only Limine's dense logical IDs; APs still parked.",
        HAL_PROOF + "\n        // Stage 2: write only Limine's dense logical IDs; APs still parked.", str(stage1.HAL))
    result = dict(stage2_source)
    result.update({
        stage1.HAL: hal,
        CPU_LOCAL: CPU_LOCAL_CODE,
        SCHEDULER: scheduler,
        HAL_INIT: hal_init,
        ASM: asm,
    })
    return stage2_source, result

def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("cosmos_checkout", type=Path)
    p.add_argument("--apply", action="store_true")
    a = p.parse_args()
    root = a.cosmos_checkout.resolve()
    if not (root / ".git").is_dir():
        fail("expected isolated upstream Cosmos git clone")
    if run_git(root, "rev-parse", "HEAD") != run_git(root, "rev-parse", "refs/tags/v3.0.95^{commit}"):
        fail("upstream is not exact v3.0.95")
    previous, intended = calculate(root)
    if (root / CPU_LOCAL).exists() and not (root / CPU_LOCAL).is_file():
        fail("CpuLocal path already occupied")
    status = set(run_git(root, "status", "--porcelain", "--untracked-files=all").splitlines())
    pristine = not status and all(not (root / p).exists() for p in (stage1.MP, CPU_LOCAL))
    permitted2 = {f" M {stage1.BOOT}", f" M {stage1.HAL}", f"?? {stage1.MP}"}
    permitted3 = permitted2 | {
        f" M {SCHEDULER}", f" M {HAL_INIT}", f" M {ASM}", f"?? {CPU_LOCAL}"
    }
    has_stage2 = status == permitted2 and all(
        (root / p).is_file() and (root / p).read_text() == content
        for p, content in previous.items()
    ) and not (root / CPU_LOCAL).exists()
    has_stage3 = status == permitted3 and all(
        (root / p).is_file() and (root / p).read_text() == content
        for p, content in intended.items()
    )
    if not pristine and not has_stage2 and not has_stage3:
        fail("unexpected or partially modified Cosmos source; refusing overwrite")
    if a.apply and (pristine or has_stage2):
        for path, code in intended.items():
            (root / path).write_text(code)
        print("[SMP3095-STAGE3] PATCHED exact Cosmos 3.0.95: BSP GS + reserved AP stacks")
    elif has_stage3:
        print("[SMP3095-STAGE3] REUSED verified Stage3 overlay")
    else:
        print("[SMP3095-STAGE3] CHECK PASS (no files modified)")
    print("[SMP3095-STAGE3] Native APs NOT started; managed SMP NOT available")

if __name__ == "__main__":
    main()
