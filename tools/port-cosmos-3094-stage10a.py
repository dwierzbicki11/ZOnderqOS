#!/usr/bin/env python3
"""Cosmos 3.0.94 Stage10a: native AP interrupted-context snapshot foundation.

Run strictly AFTER Cosmos 3.0.94 patches 1..9a and Stage9b source port.
This records an actual AP's saved RSP/RIP/RFLAGS from F2 interrupt frame,
not a managed OrionGC safepoint or a runnable managed AP thread.
"""
from __future__ import annotations
import argparse
from pathlib import Path


def exactly(text: str, anchor: str, replacement: str, path: Path) -> str:
    count = text.count(anchor)
    if count != 1:
        raise SystemExit(f"[SMP3094-STAGE10A][FAIL] {path}: "
                         f"expected one verified 3.0.94 anchor, found {count}: "
                         f"{anchor[:90]!r}")
    return text.replace(anchor, replacement)


def patch_core(text: str, path: Path) -> str:
    anchor = '    private static partial ulong NativeGetTimerStoppedLvt(uint cpuId);'
    declarations = r'''
    
    [LibraryImport("*", EntryPoint = "_native_x64_get_ap_snapshot_rsp")]
    [SuppressGCTransition]
    private static partial ulong NativeSnapshotRsp(uint cpuId);

    [LibraryImport("*", EntryPoint = "_native_x64_get_ap_snapshot_rip")]
    [SuppressGCTransition]
    private static partial ulong NativeSnapshotRip(uint cpuId);

    [LibraryImport("*", EntryPoint = "_native_x64_get_ap_snapshot_rflags")]
    [SuppressGCTransition]
    private static partial ulong NativeSnapshotRflags(uint cpuId);

    [LibraryImport("*", EntryPoint = "_native_x64_get_ap_snapshot_epoch")]
    [SuppressGCTransition]
    private static partial ulong NativeSnapshotEpoch(uint cpuId);
'''
    text = exactly(text, anchor, anchor + declarations, path)
    end = r'''        Serial.WriteString("[SMP3094-STAGE9B] PASS\n");
        return true;
    }
}'''
    new_end = r'''        Serial.WriteString("[SMP3094-STAGE9B] PASS\n");
        return true;
    }

    /// <summary>
    /// Native Stage10a: inspect AP's actual interrupted RIP/RSP/RFLAGS,
    /// captured inside F2 interrupt BEFORE Stage8 rendezvous publishes
    /// ARRIVED. No managed OrionGC roots or stop-the-world participation.
    /// </summary>
    internal static bool VerifyNativeApContextSnapshots()
    {
        if (!LimineMp.TryValidateX64(out ulong count, out _) ||
            count == 0 || count > LimineMp.MaxXApicCpuCount)
        {
            Serial.WriteString("[SMP3094-CONTEXT] invalid CPU topology.\n");
            return false;
        }

        const ulong Stage8Epochs = 4;
        for (uint cpuId = 1; cpuId < count; cpuId++)
        {
            ulong rsp = NativeSnapshotRsp(cpuId);
            ulong rip = NativeSnapshotRip(cpuId);
            ulong flags = NativeSnapshotRflags(cpuId);
            ulong epoch = NativeSnapshotEpoch(cpuId);
            // The native AP waits using STI/HLT. The original interrupt
            // frame has IF=1 and architectural RFLAGS bit 1 always set.
            // RSP is the original same-CPL kernel SP, *not* a guessed
            // return frame RSP or the current BSP stack address.
            if (epoch != Stage8Epochs || rsp == 0 || (rsp & 7UL) != 0 ||
                rip == 0 || (flags & 0x202UL) != 0x202UL)
            {
                Serial.WriteString("[SMP3094-CONTEXT] invalid AP interrupt frame cpu=");
                Serial.WriteNumber(cpuId);
                Serial.WriteString(" epoch=");
                Serial.WriteNumber(epoch);
                Serial.WriteString(" rsp=");
                Serial.WriteNumber(rsp);
                Serial.WriteString(" rip=");
                Serial.WriteNumber(rip);
                Serial.WriteString(" rflags=");
                Serial.WriteNumber(flags);
                Serial.WriteString("\n");
                return false;
            }

            Serial.WriteString("[SMP3094-CONTEXT] CPU=");
            Serial.WriteNumber(cpuId);
            Serial.WriteString(" epoch=");
            Serial.WriteNumber(epoch);
            Serial.WriteString(" rsp=");
            Serial.WriteNumber(rsp);
            Serial.WriteString(" rip=");
            Serial.WriteNumber(rip);
            Serial.WriteString(" rflags=");
            Serial.WriteNumber(flags);
            Serial.WriteString("\n");
        }
        Serial.WriteString("[SMP3094-STAGE10A] PASS\n");
        return true;
    }
}'''
    return exactly(text, end, new_end, path)


def patch_hal(text: str, path: Path) -> str:
    anchor = r'''        if (!SmpApBootstrap.RunNativePeriodicApTimerProof())
        {
            Cosmos.Kernel.Core.Panic.Halt("SMP3094 stage 9b: AP timer delivery failed.");
        }
'''
    updated = anchor + r'''
        // Stage10a: collect *native* interrupted AP contexts from the
        // prior F2 rendezvous. No managed OrionGC safepoints exist yet.
        if (!SmpApBootstrap.VerifyNativeApContextSnapshots())
        {
            Cosmos.Kernel.Core.Panic.Halt("SMP3094 stage 10a: native AP context capture failed.");
        }
'''
    return exactly(text, anchor, updated, path)


def patch_asm(text: str, path: Path) -> str:
    # AP has taken a same-privilege x64 interrupt, then pushed rax/rcx/rdx.
    # [rsp+24]=saved RIP; [rsp+40]=saved RFLAGS; original RSP=rsp+48.
    # Capture BEFORE the locked ARRIVED publication in Stage8.
    anchor = r'''    lea rdx, [rip + _smp8_barrier_state]
    shl rax, 6
    add rdx, rax
    mov rcx, qword ptr [rip + _smp8_request_generation]'''
    replacement = r'''    lea rdx, [rip + _smp8_barrier_state]
    shl rax, 6
    add rdx, rax
    // Stage10a: save the real interrupted kernel context, not BSP registers.
    // The F2 ISR has pushed rax/rcx/rdx above the hardware RIP/CS/RFLAGS.
    lea rcx, [rip + _smp10_native_context]
    mov eax, dword ptr gs:[0]
    shl rax, 6
    add rcx, rax
    lea rax, [rsp + 48]                   // interrupted kernel RSP
    mov qword ptr [rcx + 0], rax
    mov rax, qword ptr [rsp + 24]        // interrupted RIP
    mov qword ptr [rcx + 8], rax
    mov rax, qword ptr [rsp + 40]        // interrupted RFLAGS
    mov qword ptr [rcx + 16], rax
    mov rax, qword ptr [rip + _smp8_request_generation]
    mov qword ptr [rcx + 24], rax
    mfence                                // publish before Stage8 ARRIVED
    mov rcx, qword ptr [rip + _smp8_request_generation]'''
    text = exactly(text, anchor, replacement, path)

    getters_anchor = r'''SMP8_BARRIER_GETTER _native_x64_get_ap_barrier_sample, 16
// Stage 9a: read the 32-bit AP-local LVT sample from reserved bytes.
SMP8_BARRIER_GETTER _native_x64_get_ap_timer_lvt, 32'''
    getters = getters_anchor + r'''

// Stage10a: bounded, read-only BSP getters for captured native AP context.
// Slots are dense logical CPU IDs, not physical LAPIC IDs.
.macro SMP10_CONTEXT_GETTER name, offset
.global \name
\name:
    test edi, edi
    jz .Lsmp10_bad_\@
    cmp edi, 255
    ja .Lsmp10_bad_\@
    lea rcx, [rip + _smp10_native_context]
    mov eax, edi
    shl rax, 6
    add rcx, rax
    mov rax, qword ptr [rcx + \offset]
    ret
.Lsmp10_bad_\@:
    xor eax, eax
    ret
.endm

SMP10_CONTEXT_GETTER _native_x64_get_ap_snapshot_rsp, 0
SMP10_CONTEXT_GETTER _native_x64_get_ap_snapshot_rip, 8
SMP10_CONTEXT_GETTER _native_x64_get_ap_snapshot_rflags, 16
SMP10_CONTEXT_GETTER _native_x64_get_ap_snapshot_epoch, 24'''
    text = exactly(text, getters_anchor, getters, path)

    bss = r'''_smp9_timer_initial_count: .long 0'''
    bss_new = bss + r'''

// Native snapshots only; NOT managed GC's stack root registration.
// 256 cacheline-sized AP context slots: RSP/RIP/RFLAGS/F2 generation.
.balign 64
_smp10_native_context: .zero 16384'''
    return exactly(text, bss, bss_new, path)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("cosmos_root", type=Path)
    args = ap.parse_args()
    root = args.cosmos_root.resolve()
    funcs = {
        "src/Cosmos.Kernel.Core.X64/Cpu/SmpApBootstrap.cs": patch_core,
        "src/Cosmos.Kernel.HAL.X64/X64PlatformInitializer.cs": patch_hal,
        "src/Cosmos.Kernel.Native.X64/CPU/Interrupts.s": patch_asm,
    }
    modified = {}
    for relative, fn in funcs.items():
        file = root / relative
        if not file.is_file():
            raise SystemExit("[SMP3094-STAGE10A][FAIL] missing " + str(file))
        source = file.read_text()
        changed = fn(source, file)
        if source == changed:
            raise SystemExit("[SMP3094-STAGE10A][FAIL] no changes in " + str(file))
        modified[file] = changed

    # Validate all source anchors before modifying any of the 3 files.
    for file, changed in modified.items():
        file.write_text(changed)
        print("[SMP3094-STAGE10A] patched " + str(file.relative_to(root)))
    print("[SMP3094-STAGE10A] Installed native AP context snapshots; managed GC NOT enabled")


if __name__ == "__main__":
    main()
