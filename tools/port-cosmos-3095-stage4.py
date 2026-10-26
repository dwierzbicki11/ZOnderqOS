#!/usr/bin/env python3
"""Stage4: isolated x64 Limine AP entry -> private stack/GS -> native park.

No interrupts, IPI, local APIC timer, managed scheduler or GC is run on APs.
Stage4 is QEMU-only and NOT a Stage23 managed SMP implementation.
"""
from __future__ import annotations

import argparse
import importlib.util
from pathlib import Path
import subprocess

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("smp3095_stage3", HERE / "port-cosmos-3095-stage3.py")
if spec is None or spec.loader is None:
    raise SystemExit("[SMP3095-STAGE4][FAIL] Stage3 port unavailable")
stage3 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(stage3)
stage1 = stage3.stage1

NATIVE_AP = Path("src/Cosmos.Kernel.Core/CPU/NativeApStartup.cs")

NATIVE_AP_SOURCE = r'''using System.Runtime.InteropServices;

namespace Cosmos.Kernel.Core.CPU;

/// <summary>Native AP readiness only. Never dispatch managed work in Stage4.</summary>
internal static partial class CpuLocal
{
#if ARCH_X64
    [LibraryImport("*", EntryPoint = "_native_x64_smp3095_ap_entry_address")]
    [SuppressGCTransition]
    private static partial ulong NativeApEntryAddress();

    [LibraryImport("*", EntryPoint = "_native_x64_smp3095_ap_is_online")]
    [SuppressGCTransition]
    private static partial uint NativeApIsOnline(uint logicalId);

    internal static ulong ParkedApEntryAddress => NativeApEntryAddress();
    internal static bool IsNativeApOnline(uint logicalId) =>
        logicalId != 0 && logicalId < 256 && NativeApIsOnline(logicalId) == 1;
#else
    internal static ulong ParkedApEntryAddress => 0;
    internal static bool IsNativeApOnline(uint logicalId) => false;
#endif
}
'''

METHOD = r'''
    /// <summary>
    /// Stage4 native-only AP launch. Preflights the whole dense map before
    /// publishing any Limine goto pointer. Only the BSP invokes this method.
    /// AP entry is a native assembly function that never calls managed code.
    /// </summary>
    public static bool TryPublishNativeParkedAps(ulong entry, out ulong cpuCount)
    {
        cpuCount = 0;
        if (entry == 0 || !TryValidateX64(out ulong count, out uint bspLapicId))
        {
            return false;
        }

        LimineMpResponseX64* response = (LimineMpResponseX64*)Limine.MP.Response;
        ulong expectedApId = 1;
        for (ulong i = 0; i < count; i++)
        {
            LimineMpInfoX64* desc = response->Cpus[i];
            ulong expected = desc->LapicId == bspLapicId ? 0 : expectedApId++;
            if (desc->ExtraArgument != expected || desc->GotoAddress != null)
            {
                return false;
            }
        }

        if (expectedApId != count)
        {
            return false;
        }

        // Limine observes GotoAddress and enters the AP using SysV rdi=descriptor.
        // ExtraArgument and the reserved stacks are initialized before publish.
        System.Threading.Thread.MemoryBarrier();
        for (ulong i = 0; i < count; i++)
        {
            LimineMpInfoX64* desc = response->Cpus[i];
            if (desc->LapicId != bspLapicId)
            {
                desc->GotoAddress = (delegate* unmanaged<LimineMpInfoX64*, void>)entry;
            }
        }

        cpuCount = count;
        return true;
    }
'''

# Limine MP ABI: uint processor_id at 0, uint lapic_id at 4,
# uint64 reserved at 8, goto_address at 16, extra_argument at 24.
# AP enters with rdi pointing at its own limine_mp_info. This native path
# avoids every managed/runtime, IDT, LAPIC and IRQ/scheduler dependency.
ASM_AP = r'''// Stage4: return the SysV Limine AP entry pointer to the BSP.
.global _native_x64_smp3095_ap_entry_address
_native_x64_smp3095_ap_entry_address:
    lea rax, [rip + _native_x64_smp3095_park_ap]
    ret

.global _native_x64_smp3095_ap_is_online
_native_x64_smp3095_ap_is_online:
    test edi, edi
    jz .Lsmp3095_ap_offline
    cmp edi, 255
    ja .Lsmp3095_ap_offline
    lea rax, [rip + _smp3095_cpu_slots]
    mov ecx, edi
    shl rcx, 6
    add rax, rcx
    mov eax, dword ptr [rax + 8]
    ret
.Lsmp3095_ap_offline:
    xor eax, eax
    ret

// Limine transfers AP control here, NOT through NativeAOT managed code.
.global _native_x64_smp3095_park_ap
_native_x64_smp3095_park_ap:
    cli
    mov r10, qword ptr [rdi + 24]  // descriptor ExtraArgument: dense ID
    test r10, r10
    jz .Lsmp3095_invalid_ap
    cmp r10, 255
    ja .Lsmp3095_invalid_ap

    // Do not use the Limine transient stack after this point.
    lea rsp, [rip + _smp3095_ap_stacks]
    mov rax, r10
    shl rax, 14
    add rsp, rax
    add rsp, 16384
    and rsp, -16

    // Unique cache-line GS slot: logical ID at +0, online flag at +8.
    lea r8, [rip + _smp3095_cpu_slots]
    mov rax, r10
    shl rax, 6
    add r8, rax
    mov qword ptr [r8], r10
    mov rax, r8
    mov rdx, rax
    shr rdx, 32
    mov ecx, 0xC0000101
    wrmsr
    mov eax, dword ptr gs:[0]
    cmp eax, r10d
    jne .Lsmp3095_invalid_ap
    mov dword ptr [r8 + 8], 1  // x86 TSO release store; BSP polls each slot

.Lsmp3095_park_native:
    cli
    hlt
    jmp .Lsmp3095_park_native
.Lsmp3095_invalid_ap:
    cli
.Lsmp3095_invalid_halt:
    hlt
    jmp .Lsmp3095_invalid_halt

'''

HAL_PROOF = r'''
        // Stage4: each AP must execute real native assembly on its own
        // 16 KiB stack and publish its GS-based logical CPU identity.
        // APs park with interrupts disabled: no managed execution occurs.
        if (!LimineMp.TryPublishNativeParkedAps(CpuLocal.ParkedApEntryAddress,
                                                 out ulong nativeCpuCount))
        {
            Serial.WriteString("[SMP3095-STAGE4] FAIL AP_PARKED MANAGED_OFF\n");
            Cosmos.Kernel.Core.Panic.Halt("SMP3095 Stage4 AP preflight failed.");
        }

        ulong observedAps = 0;
        for (uint logical = 1; logical < nativeCpuCount; logical++)
        {
            bool arrived = false;
            for (uint retry = 0; retry < 1000000; retry++)
            {
                if (CpuLocal.IsNativeApOnline(logical))
                {
                    arrived = true;
                    break;
                }
                System.Threading.Thread.SpinWait(8);
            }

            if (!arrived)
            {
                Serial.WriteString("[SMP3095-STAGE4] FAIL AP_TIMEOUT MANAGED_OFF\n");
                Cosmos.Kernel.Core.Panic.Halt("SMP3095 Stage4 AP rendezvous timed out.");
            }

            observedAps++;
        }

        Serial.WriteString("[SMP3095-STAGE4] cpus=");
        Serial.WriteNumber(nativeCpuCount);
        Serial.WriteString(" native_ap_online=");
        Serial.WriteNumber(observedAps);
        Serial.WriteString(" AP_PARKED MANAGED_OFF\n");
'''

def fail(message: str) -> None:
    raise SystemExit(f"[SMP3095-STAGE4][FAIL] {message}")

def git(root: Path, *args: str) -> str:
    p = subprocess.run(["git", "-C", str(root), *args],
                       text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if p.returncode:
        fail(f"git {' '.join(args)}: {p.stderr.strip()}")
    return p.stdout.rstrip("\n")

def calculate(root: Path) -> tuple[dict[Path, str], dict[Path, str]]:
    _, previous = stage3.calculate(root)
    source = dict(previous)
    mp = previous[stage1.MP]
    tail = "        return nextId == count;\n    }\n}\n"
    if mp.count(tail) != 1:
        fail("Stage2 Limine MP method tail changed")
    source[stage1.MP] = mp.replace(
        tail, "        return nextId == count;\n    }\n" + METHOD + "}\n", 1)

    hal = previous[stage1.HAL]
    anchor = r'''            Serial.WriteString("[SMP3095-STAGE1] UNAVAILABLE AP_NOT_STARTED\n");
        }
'''
    if hal.count(anchor) != 1:
        fail("Stage1 HAL diagnostic tail changed")
    source[stage1.HAL] = hal.replace(anchor, anchor + HAL_PROOF, 1)
    asm = previous[stage3.ASM]
    source[stage3.ASM] = stage1.replace_exact(
        asm, "// void _native_set_context_switch_sp",
        ASM_AP + "// void _native_set_context_switch_sp", str(stage3.ASM))
    source[NATIVE_AP] = NATIVE_AP_SOURCE
    return previous, source

def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("cosmos_checkout", type=Path)
    p.add_argument("--apply", action="store_true")
    args = p.parse_args()
    root = args.cosmos_checkout.resolve()
    if not (root / ".git").is_dir():
        fail("requires private exact-tag Cosmos checkout")
    if git(root, "rev-parse", "HEAD") != git(root, "rev-parse", "refs/tags/v3.0.95^{commit}"):
        fail("refusing source other than exact Cosmos v3.0.95")
    previous, desired = calculate(root)
    allowed3 = {
        f" M {stage1.BOOT}", f" M {stage1.HAL}", f"?? {stage1.MP}",
        f" M {stage3.SCHEDULER}", f" M {stage3.HAL_INIT}",
        f" M {stage3.ASM}", f"?? {stage3.CPU_LOCAL}"
    }
    allowed4 = allowed3 | {f"?? {NATIVE_AP}"}
    status = set(git(root, "status", "--porcelain", "--untracked-files=all").splitlines())
    pristine = not status and all(not (root / path).exists()
                                  for path in (stage1.MP, stage3.CPU_LOCAL, NATIVE_AP))
    stage3_ready = status == allowed3 and not (root / NATIVE_AP).exists() and all(
        (root / path).is_file() and (root / path).read_text() == value
        for path, value in previous.items())
    stage4_ready = status == allowed4 and all(
        (root / path).is_file() and (root / path).read_text() == value
        for path, value in desired.items())
    if not (pristine or stage3_ready or stage4_ready):
        fail("unrecognized source mutations: refusing partial/unreviewed overlay")
    if args.apply and (pristine or stage3_ready):
        for path, value in desired.items():
            (root / path).write_text(value)
        print("[SMP3095-STAGE4] PATCHED strict native AP entry; managed APs disabled")
    elif stage4_ready:
        print("[SMP3095-STAGE4] REUSED exact verified overlay")
    else:
        print("[SMP3095-STAGE4] CHECK PASS: reviewed source anchors only")
    print("[SMP3095-STAGE4] QEMU ONLY; not managed Stage23")

if __name__ == "__main__":
    main()
