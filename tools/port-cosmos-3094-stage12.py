#!/usr/bin/env python3
"""Cosmos 3.0.94 Stage12 (experimental): one bounded allocation-free C# AP call.

Strict prerequisite: stages 1..9a plus stage9b and stage10a source ports.
This is NOT System.Threading.Thread scheduling, OrionGC concurrent safety,
GC registration or preemptive managed SMP. Only run in the isolated probe ISO.
"""
from __future__ import annotations
import argparse
from pathlib import Path


def one(src: str, old: str, new: str, path: Path) -> str:
    count = src.count(old)
    if count != 1:
        raise SystemExit(f"[SMP3094-STAGE12][FAIL] source ABI drift in {path}, "
                         f"expected exact anchor once, got {count}: {old[:80]!r}")
    return src.replace(old, new)


MANAGED = r'''#if ARCH_X64
using System;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.Runtime;

namespace Cosmos.Kernel.Core.Bridge;

/// <summary>
/// One native-to-managed AP crossing in a NO-ALLOCATION boot probe.
/// Does not enroll a managed Thread or its GC stack roots! No concurrent GC.
/// </summary>
internal static partial class ManagedApNative
{
    [LibraryImport("*", EntryPoint = "_native_x64_publish_smp12_checksum")]
    [SuppressGCTransition]
    private static partial int Publish(uint cpuId, ulong checksum);

    [UnmanagedCallersOnly(EntryPoint = "__cosmos_smp3094_managed_ap_entry")]
    public static int Execute(uint requestedCpu)
    {
        StartupCodeHelpers.RhpReversePInvoke(IntPtr.Zero);
        uint cpuId = CpuLocal.CurrentCpuId;
        int status = cpuId == 0 || cpuId != requestedCpu ? 1 : 0;
        if (status == 0)
        {
            ulong checksum = 0xD6E8FEB86659FD93UL ^ cpuId;
            for (uint round = 0; round < 1024; round++)
            {
                checksum ^= (ulong)(cpuId + round) * 0x9E3779B97F4A7C15UL;
                checksum = (checksum << 13) | (checksum >> 51);
            }
            if (Publish(cpuId, checksum) != 0)
            {
                status = 2;
            }
        }
        StartupCodeHelpers.RhpReversePInvokeReturn(IntPtr.Zero);
        return status;
    }
}
#endif
'''


def core(src: str, path: Path) -> str:
    anchor = r'''    private static partial ulong NativeSnapshotEpoch(uint cpuId);'''
    imports = r'''
    
    [LibraryImport("*", EntryPoint = "_native_x64_get_smp12_state")]
    [SuppressGCTransition]
    private static partial uint NativeGetManagedProofState(uint cpuId);

    [LibraryImport("*", EntryPoint = "_native_x64_get_smp12_checksum")]
    [SuppressGCTransition]
    private static partial ulong NativeGetManagedProofChecksum(uint cpuId);
'''
    src = one(src, anchor, anchor + imports, path)

    anchor2 = r'''        Serial.WriteString("[SMP3094-STAGE10A] PASS\n");
        return true;
    }
}'''
    proof = r'''        Serial.WriteString("[SMP3094-STAGE10A] PASS\n");
        return true;
    }

    private static ulong ExpectedManagedChecksum(uint cpuId)
    {
        ulong value = 0xD6E8FEB86659FD93UL ^ cpuId;
        for (uint round = 0; round < 1024; round++)
        {
            value ^= (ulong)(cpuId + round) * 0x9E3779B97F4A7C15UL;
            value = (value << 13) | (value >> 51);
        }
        return value;
    }

    /// <summary>
    /// Execute exactly one allocation-free managed function on each AP after
    /// the native Stage9b timer has been stopped. Boot-only diagnostic.
    /// NOT managed Thread.Start, scheduler residency or GC-safe concurrency.
    /// </summary>
    internal static bool RunBoundedManagedApProof()
    {
        const ulong TimeoutSeconds = 8;
        if (!LimineMp.TryValidateX64(out ulong count, out _) ||
            count == 0 || count > LimineMp.MaxXApicCpuCount)
        {
            return false;
        }

        if (count == 1)
        {
            Serial.WriteString("[SMP3094-STAGE12] PASS\n");
            return true;
        }

        long frequency = X64CpuOps.TscFrequency;
        ulong perSecond = frequency > 0 ? (ulong)frequency : 1_000_000_000UL;
        if (perSecond > ulong.MaxValue / TimeoutSeconds)
        {
            return false;
        }
        ulong timeoutTicks = perSecond * TimeoutSeconds;
        LimineMpResponseX64* response = (LimineMpResponseX64*)Limine.MP.Response;

        for (uint cpuId = 1; cpuId < count; cpuId++)
        {
            // Stage9b F1 controller command 3 only releases one
            // native-parked AP for the isolated allocation-free C# export.
            if (NativeTimerCommand(response->Cpus, count, cpuId,
                                   3, 0, timeoutTicks) != 1)
            {
                Serial.WriteString("[SMP3094-MANAGED-ONESHOT] F1 dispatch failed.\n");
                return false;
            }
            ulong start = X64CpuOps.ReadTSC();
            uint state;
            do
            {
                state = NativeGetManagedProofState(cpuId);
                if (state == 1 || state == 2)
                    break;
            } while (X64CpuOps.ReadTSC() - start < timeoutTicks);

            ulong observed = NativeGetManagedProofChecksum(cpuId);
            if (state != 1 || observed != ExpectedManagedChecksum(cpuId))
            {
                Serial.WriteString("[SMP3094-MANAGED-ONESHOT] checksum/state failure cpu=");
                Serial.WriteNumber(cpuId);
                Serial.WriteString(" state=");
                Serial.WriteNumber(state);
                Serial.WriteString("\n");
                return false;
            }
            Serial.WriteString("[SMP3094-MANAGED-ONESHOT] CPU=");
            Serial.WriteNumber(cpuId);
            Serial.WriteString(" checksum=");
            Serial.WriteNumber(observed);
            Serial.WriteString(" returned-native-park=1\n");
        }

        Serial.WriteString("[SMP3094-STAGE12] PASS\n");
        return true;
    }
}'''
    return one(src, anchor2, proof, path)


def hal(src: str, path: Path) -> str:
    anchor = r'''        if (!SmpApBootstrap.VerifyNativeApContextSnapshots())
        {
            Cosmos.Kernel.Core.Panic.Halt("SMP3094 stage 10a: native AP context capture failed.");
        }
'''
    result = anchor + r'''
        // Stage12 isolated boot proof: NO managed allocation, no Thread.Start,
        // no concurrent managed GC, return every AP to native park.
        if (!SmpApBootstrap.RunBoundedManagedApProof())
        {
            Cosmos.Kernel.Core.Panic.Halt("SMP3094 stage 12: managed AP one-shot failed.");
        }
'''
    return one(src, anchor, result, path)


def asm(src: str, path: Path) -> str:
    src = one(src,
        '.extern __managed__irq',
        '.extern __managed__irq\n.extern __cosmos_smp3094_managed_ap_entry',
        path)

    # Guard malformed Limine entry: r13 is initially 0 even if the early
    # CPU descriptor validation branches straight into the HLT loop.
    src = one(src, '_native_x64_ap_entry:\n    cli\n    cld\n',
                   '_native_x64_ap_entry:\n    cli\n    cld\n    xor r13d, r13d\n', path)
    park = r'''.Lsmp4_park:
    // STI;HLT defers interrupt delivery until HLT is entered, avoiding
    // the classic lost-wakeup race. No managed handlers run on APs.
    sti
    hlt
    jmp .Lsmp4_park'''
    modified = r'''.Lsmp4_park:
    // Native AP is quiescent here. Stage12 ONLY executes after the BSP
    // has finished native timer tests and sends an explicit F1 command 3.
    sti
    hlt
    cli
    test r13, r13
    jz .Lsmp4_park
    cmp r13, 255
    ja .Lsmp4_park
    lea rcx, [rip + _smp9_timer_state]
    mov rax, r13
    shl rax, 6
    add rcx, rax
    cmp dword ptr [rcx + 0], 3
    jne .Lsmp4_park
    xor eax, eax
    xchg dword ptr [rcx + 0], eax
    // SysV call with the AP's own 16KiB stack, no dynamic GC roots.
    mov edi, r13d
    call __cosmos_smp3094_managed_ap_entry
    mov r10d, eax
    lea rcx, [rip + _smp12_managed_state]
    mov rax, r13
    shl rax, 6
    add rcx, rax
    test r10d, r10d
    jne .Lsmp12_failed
    mov eax, 1
    xchg dword ptr [rcx + 0], eax
    jmp .Lsmp4_park
.Lsmp12_failed:
    mov eax, 2
    xchg dword ptr [rcx + 0], eax
    jmp .Lsmp4_park'''
    src = one(src, park, modified, path)

    handler = r'''    cmp ecx, 2
    je .Lsmp9_timer_stop
    // Unmodified Stage 6/7 F1 semantics when there is no timer command.'''
    handler2 = r'''    cmp ecx, 2
    je .Lsmp9_timer_stop
    cmp ecx, 3
    je .Lsmp12_dispatch_f1
    // Unmodified Stage 6/7 F1 semantics when there is no timer command.'''
    src = one(src, handler, handler2, path)

    before_start = r'''.Lsmp9_timer_start:
    mov rcx, qword ptr [rip + _smp6_lapic_base]'''
    dispatch = r'''.Lsmp12_dispatch_f1:
    // Command 3: acknowledge receipt, but leave command set so the
    // *native park loop* executes C# outside interrupt context.
    mfence
    mov dword ptr [rdx + 4], 3
    jmp .Lsmp6_handler_eoi

.Lsmp9_timer_start:
    mov rcx, qword ptr [rip + _smp6_lapic_base]'''
    src = one(src, before_start, dispatch, path)

    acceptance = r'''    cmp ecx, 1
    je .Lsmp9_cmd_valid
    cmp ecx, 2
    jne .Lsmp9_invalid
.Lsmp9_cmd_valid:'''
    new_acceptance = r'''    cmp ecx, 1
    je .Lsmp9_cmd_valid
    cmp ecx, 2
    je .Lsmp9_cmd_valid
    cmp ecx, 3
    jne .Lsmp9_invalid
.Lsmp9_cmd_valid:'''
    src = one(src, acceptance, new_acceptance, path)

    hook = r'''// BSP: rdi=Limine CPU descriptors, rsi=count, rdx=single global TSC'''
    exports = r'''// Stage12: per-CPU status and checksum of one C# AP invocation.
// Slot layout: 64 bytes per CPU, state at 0 and 64-bit checksum at +8.
.global _native_x64_publish_smp12_checksum
_native_x64_publish_smp12_checksum:
    test edi, edi
    jz .Lsmp12_publish_bad
    cmp edi, 255
    ja .Lsmp12_publish_bad
    cmp edi, dword ptr gs:[0]
    jne .Lsmp12_publish_bad
    lea rcx, [rip + _smp12_managed_state]
    mov eax, edi
    shl rax, 6
    mov qword ptr [rcx + rax + 8], rsi
    mfence
    xor eax, eax
    ret
.Lsmp12_publish_bad:
    mov eax, -1
    ret

.macro SMP12_GETTER name, offset, width
.global \name
\name:
    test edi, edi
    jz .Lsmp12_bad_\@
    cmp edi, 255
    ja .Lsmp12_bad_\@
    lea rcx, [rip + _smp12_managed_state]
    mov eax, edi
    shl rax, 6
    add rcx, rax
    .if \width == 4
        mov eax, dword ptr [rcx + \offset]
    .else
        mov rax, qword ptr [rcx + \offset]
    .endif
    ret
.Lsmp12_bad_\@:
    xor eax, eax
    ret
.endm

SMP12_GETTER _native_x64_get_smp12_state, 0, 4
SMP12_GETTER _native_x64_get_smp12_checksum, 8, 8

// BSP: rdi=Limine CPU descriptors, rsi=count, rdx=single global TSC'''
    src = one(src, hook, exports, path)

    bss = r'''_smp10_native_context: .zero 16384'''
    bss_new = bss + r'''

// Stage12 isolated non-allocating AP managed-checksum report slots.
.balign 64
_smp12_managed_state: .zero 16384'''
    return one(src, bss, bss_new, path)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("cosmos_root", type=Path)
    args = parser.parse_args()
    root = args.cosmos_root.resolve()
    functions = {
        "src/Cosmos.Kernel.Core.X64/Cpu/SmpApBootstrap.cs": core,
        "src/Cosmos.Kernel.HAL.X64/X64PlatformInitializer.cs": hal,
        "src/Cosmos.Kernel.Native.X64/CPU/Interrupts.s": asm,
    }
    output = {}
    for rel, function in functions.items():
        file = root / rel
        if not file.is_file():
            raise SystemExit("[SMP3094-STAGE12][FAIL] missing " + str(file))
        original = file.read_text()
        updated = function(original, file)
        if updated == original:
            raise SystemExit("[SMP3094-STAGE12][FAIL] unchanged " + rel)
        output[file] = updated
    managed = root / "src/Cosmos.Kernel.Core/Bridge/Export/ManagedApNative.cs"
    if managed.exists():
        raise SystemExit("[SMP3094-STAGE12][FAIL] AP export already exists; cannot overwrite")
    # All exact source-anchor verification precedes ALL writes.
    for path, contents in output.items():
        path.write_text(contents)
        print("[SMP3094-STAGE12] patched " + str(path.relative_to(root)))
    managed.write_text(MANAGED)
    print("[SMP3094-STAGE12] exported allocation-free managed C# AP probe")
    print("[SMP3094-STAGE12] NOT Thread.Start; OrionGC concurrency forbidden")


if __name__ == "__main__":
    main()
