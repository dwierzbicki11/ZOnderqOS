#!/usr/bin/env python3
"""Port the native Stage 9b LAPIC timer proof onto *patched Cosmos v3.0.94*.

Run only after patches/cosmos-3094/0001..0009. Every edit is guarded by an
exact source anchor; do not silently fall back to a different Cosmos ABI.
The Stage9b AP timer remains 100% native, with no managed AP/OrionGC work.
"""
from __future__ import annotations

import argparse
from pathlib import Path


def replace_once(text: str, old: str, new: str, path: Path) -> str:
    occurrences = text.count(old)
    if occurrences != 1:
        raise SystemExit(
            f"[SMP3094-STAGE9B][FAIL] {path}: expected exactly one "
            f"3.0.94 anchor, found {occurrences}: {old[:125]!r}"
        )
    return text.replace(old, new)


def patch_core(text: str, path: Path) -> str:
    anchor = '    private static partial ulong NativeGetApTimerLvt(uint cpuId);'
    declarations = r'''
    
    [LibraryImport("*", EntryPoint = "_native_x64_timer_command")]
    [SuppressGCTransition]
    private static partial int NativeTimerCommand(
        LimineMpInfoX64** descriptors, ulong cpuCount, uint cpuId,
        uint command, uint initialCount, ulong timeoutTicks);

    [LibraryImport("*", EntryPoint = "_native_x64_timer_wait_ticks")]
    [SuppressGCTransition]
    private static partial int NativeWaitTimerTicks(
        uint cpuId, uint minimumTicks, ulong timeoutTicks);

    [LibraryImport("*", EntryPoint = "_native_x64_timer_wait_quiet")]
    [SuppressGCTransition]
    private static partial int NativeWaitTimerQuiet(uint cpuId, ulong quietTicks);

    [LibraryImport("*", EntryPoint = "_native_x64_get_ap_periodic_ticks")]
    [SuppressGCTransition]
    private static partial ulong NativeGetPeriodicTicks(uint cpuId);

    [LibraryImport("*", EntryPoint = "_native_x64_get_ap_timer_lvt_after_stop")]
    [SuppressGCTransition]
    private static partial ulong NativeGetTimerStoppedLvt(uint cpuId);
'''
    text = replace_once(text, anchor, anchor + declarations, path)

    before_end = r'''        Serial.WriteString("[SMP3094-STAGE9A] PASS\n");
        return true;
    }
}'''
    after_end = r'''        Serial.WriteString("[SMP3094-STAGE9A] PASS\n");
        return true;
    }

    /// <summary>
    /// Stage 9b: prove real periodic xAPIC timer interrupts on *every* AP,
    /// one AP at a time through native F1 commands, then disarm on success
    /// AND failure before returning to the managed BSP. No AP C#/OrionGC.
    /// </summary>
    internal static bool RunNativePeriodicApTimerProof()
    {
        const uint PeriodMs = 10;
        const uint MinimumTicks = 3;
        const ulong TimeoutSeconds = 12;
        const ulong QuietMilliseconds = 30;

        if (!LocalApic.IsInitialized || !LocalApic.IsTimerCalibrated ||
            LocalApic.TicksPerMs == 0 ||
            LocalApic.TicksPerMs > uint.MaxValue / PeriodMs ||
            !LimineMp.TryValidateX64(out ulong count, out _) ||
            count == 0 || count > LimineMp.MaxXApicCpuCount)
        {
            Serial.WriteString("[SMP3094-TIMER-B] invalid LAPIC calibration / topology.\n");
            return false;
        }

        long frequency = X64CpuOps.TscFrequency;
        ulong tscPerSecond = frequency > 0 ? (ulong)frequency : 1_000_000_000UL;
        if (tscPerSecond > ulong.MaxValue / TimeoutSeconds ||
            tscPerSecond > ulong.MaxValue / QuietMilliseconds)
        {
            Serial.WriteString("[SMP3094-TIMER-B] TSC calibration overflow.\n");
            return false;
        }

        ulong timeoutTicks = tscPerSecond * TimeoutSeconds;
        ulong quietTicks = tscPerSecond * QuietMilliseconds / 1000UL;
        uint initialCount = LocalApic.TicksPerMs * PeriodMs;
        if (quietTicks == 0 || initialCount == 0)
            return false;

        LimineMpResponseX64* response = (LimineMpResponseX64*)Limine.MP.Response;
        for (uint cpuId = 1; cpuId < count; cpuId++)
        {
            // The stop command is attempted EVEN IF start or tick-wait fails.
            // No managed execution runs on the AP during this proof.
            int started = NativeTimerCommand(response->Cpus, count, cpuId, 1, initialCount, timeoutTicks);
            int observed = started == 1
                ? NativeWaitTimerTicks(cpuId, MinimumTicks, timeoutTicks)
                : -2;
            int stopped = NativeTimerCommand(response->Cpus, count, cpuId, 2, 0, timeoutTicks);
            int quiet = stopped == 1
                ? NativeWaitTimerQuiet(cpuId, quietTicks)
                : -2;
            ulong ticks = NativeGetPeriodicTicks(cpuId);
            ulong stoppedLvt = NativeGetTimerStoppedLvt(cpuId);
            ulong legacyIpi = NativeGetIpiCount(cpuId);

            if (started != 1 || observed != 1 || stopped != 1 || quiet != 1 ||
                ticks < MinimumTicks || stoppedLvt != 0x000100F3UL ||
                legacyIpi != 33)
            {
                Serial.WriteString("[SMP3094-TIMER-B] FAIL CPU=");
                Serial.WriteNumber(cpuId);
                Serial.WriteString(" start=");
                Serial.WriteNumber((ulong)(uint)started);
                Serial.WriteString(" wait=");
                Serial.WriteNumber((ulong)(uint)observed);
                Serial.WriteString(" stop=");
                Serial.WriteNumber((ulong)(uint)stopped);
                Serial.WriteString(" quiet=");
                Serial.WriteNumber((ulong)(uint)quiet);
                Serial.WriteString(" ticks=");
                Serial.WriteNumber(ticks);
                Serial.WriteString(" lvt=");
                Serial.WriteNumber(stoppedLvt);
                Serial.WriteString("\n");
                return false;
            }

            Serial.WriteString("[SMP3094-TIMER-B] CPU=");
            Serial.WriteNumber(cpuId);
            Serial.WriteString(" ticks=");
            Serial.WriteNumber(ticks);
            Serial.WriteString(" stopped=1 lvt=");
            Serial.WriteNumber(stoppedLvt);
            Serial.WriteString("\n");
        }

        Serial.WriteString("[SMP3094-STAGE9B] PASS\n");
        return true;
    }
}'''
    return replace_once(text, before_end, after_end, path)


def patch_hal(text: str, path: Path) -> str:
    anchor = r'''        if (!SmpApBootstrap.VerifyApTimerReadiness())
        {
            Cosmos.Kernel.Core.Panic.Halt("SMP3094 stage 9a: AP timer readiness failed.");
        }
'''
    replacement = anchor + r'''
        // Stage 9b: actual native LAPIC periodic interrupt delivery per AP.
        // Arm, observe >=3 real ticks, mask/stop and confirm quiescence
        // BEFORE managed scheduler/OrionGC can ever touch the AP.
        if (!SmpApBootstrap.RunNativePeriodicApTimerProof())
        {
            Cosmos.Kernel.Core.Panic.Halt("SMP3094 stage 9b: AP timer delivery failed.");
        }
'''
    return replace_once(text, anchor, replacement, path)


def patch_asm(text: str, path: Path) -> str:
    idt = r'''    mov edi, 242
    lea rsi, [rip + _smp8_ap_rendezvous_handler]
    call _smp6_set_gate
    mov eax, 1
    ret'''
    new_idt = r'''    mov edi, 242
    lea rsi, [rip + _smp8_ap_rendezvous_handler]
    call _smp6_set_gate
    // Stage 9b timer vector F3; native-only, separate from F1/F2.
    mov edi, 243
    lea rsi, [rip + _smp9_ap_timer_handler]
    call _smp6_set_gate
    mov eax, 1
    ret'''
    text = replace_once(text, idt, new_idt, path)

    old_f1 = r'''.global _smp6_ap_ipi_handler
_smp6_ap_ipi_handler:
    push rax
    push rdx
    mov eax, dword ptr gs:[0]
    cmp eax, 255
    ja .Lsmp6_handler_eoi
    lea rdx, [rip + _smp3094_ap_boot_state]
    shl rax, 6
    add rdx, rax
    lock inc qword ptr [rdx + 56]
.Lsmp6_handler_eoi:
    mov rax, qword ptr [rip + _smp6_lapic_base]
    mov dword ptr [rax + 0xB0], 0
    pop rdx
    pop rax
    iretq'''
    new_f1 = r'''.global _smp6_ap_ipi_handler
_smp6_ap_ipi_handler:
    push rax
    push rcx
    push rdx
    mov eax, dword ptr gs:[0]
    test eax, eax
    jz .Lsmp6_handler_eoi
    cmp eax, 255
    ja .Lsmp6_handler_eoi
    lea rdx, [rip + _smp9_timer_state]
    shl rax, 6
    add rdx, rax
    mov ecx, dword ptr [rdx + 0]
    cmp ecx, 1
    je .Lsmp9_timer_start
    cmp ecx, 2
    je .Lsmp9_timer_stop
    // Unmodified Stage 6/7 F1 semantics when there is no timer command.
    mov eax, dword ptr gs:[0]
    lea rdx, [rip + _smp3094_ap_boot_state]
    shl rax, 6
    add rdx, rax
    lock inc qword ptr [rdx + 56]
    jmp .Lsmp6_handler_eoi

.Lsmp9_timer_start:
    mov rcx, qword ptr [rip + _smp6_lapic_base]
    test rcx, rcx
    jz .Lsmp6_handler_eoi
    mov dword ptr [rcx + 0x3E0], 0x3      // divide LAPIC timer by 16
    mov dword ptr [rcx + 0x320], 0x200F3 // periodic, unmasked F3
    mov eax, dword ptr [rip + _smp9_timer_initial_count]
    mov dword ptr [rcx + 0x380], eax
    mov dword ptr [rdx + 16], 0x200F3
    mfence
    mov dword ptr [rdx + 0], 0
    mov dword ptr [rdx + 4], 1
    jmp .Lsmp6_handler_eoi

.Lsmp9_timer_stop:
    mov rcx, qword ptr [rip + _smp6_lapic_base]
    test rcx, rcx
    jz .Lsmp6_handler_eoi
    mov dword ptr [rcx + 0x320], 0x100F3 // masked, nonperiodic
    mov dword ptr [rcx + 0x380], 0
    mov dword ptr [rdx + 16], 0x100F3
    mfence
    mov dword ptr [rdx + 0], 0
    mov dword ptr [rdx + 4], 2
.Lsmp6_handler_eoi:
    mov rax, qword ptr [rip + _smp6_lapic_base]
    mov dword ptr [rax + 0xB0], 0
    pop rdx
    pop rcx
    pop rax
    iretq

// Stage 9b F3 IRQ executes on the AP only. No C#, allocation or GC.
.global _smp9_ap_timer_handler
_smp9_ap_timer_handler:
    push rax
    push rdx
    mov eax, dword ptr gs:[0]
    test eax, eax
    jz .Lsmp9_timer_eoi
    cmp eax, 255
    ja .Lsmp9_timer_eoi
    lea rdx, [rip + _smp9_timer_state]
    shl rax, 6
    add rdx, rax
    lock inc qword ptr [rdx + 8]
.Lsmp9_timer_eoi:
    mov rax, qword ptr [rip + _smp6_lapic_base]
    mov dword ptr [rax + 0xB0], 0
    pop rdx
    pop rax
    iretq'''
    text = replace_once(text, old_f1, new_f1, path)

    before_rendezvous = r'''// BSP: rdi=Limine CPU descriptors, rsi=count, rdx=single global TSC'''
    commands = r'''// Stage 9b native per-CPU controller; called on BSP only.
// SysV: rdi=Limine** table, rsi=count, edx=logical CPU ID,
// ecx=command (1 start, 2 stop), r8d=initial count, r9=timeout TSC cycles.
// Returns 1=acknowledged, -1=timeout, -2=invalid state.
// Only one AP is targeted at once; the stop command may override a failed
// pending start. Every command uses the *physical* LAPIC ID in Limine.
.global _native_x64_timer_command
_native_x64_timer_command:
    push rbx
    push r12
    push r13
    push r14
    push r15
    sub rsp, 32
    mov dword ptr [rsp], r8d
    mov qword ptr [rsp + 8], r9
    mov qword ptr [rsp + 16], 0
    test rdi, rdi
    jz .Lsmp9_invalid
    cmp rsi, 2
    jb .Lsmp9_invalid
    cmp rsi, 256
    ja .Lsmp9_invalid
    test r9, r9
    jz .Lsmp9_invalid
    test edx, edx
    jz .Lsmp9_invalid
    cmp rdx, rsi
    jae .Lsmp9_invalid
    cmp ecx, 1
    je .Lsmp9_cmd_valid
    cmp ecx, 2
    jne .Lsmp9_invalid
.Lsmp9_cmd_valid:
    cmp ecx, 1
    jne .Lsmp9_cmd_ready
    test r8d, r8d
    jz .Lsmp9_invalid
.Lsmp9_cmd_ready:
    mov r12, rdi
    mov r13, rsi
    mov ebx, edx
    mov r14d, ecx
    xor r10d, r10d
.Lsmp9_find_cpu:
    cmp r10, r13
    jae .Lsmp9_found
    mov r11, qword ptr [r12 + r10 * 8]
    test r11, r11
    jz .Lsmp9_invalid
    cmp qword ptr [r11 + 24], rbx
    jne .Lsmp9_find_next
    cmp qword ptr [rsp + 16], 0
    jne .Lsmp9_invalid
    mov eax, dword ptr [r11 + 4]
    cmp eax, 255
    ja .Lsmp9_invalid
    mov qword ptr [rsp + 16], r11
.Lsmp9_find_next:
    inc r10
    jmp .Lsmp9_find_cpu
.Lsmp9_found:
    cmp qword ptr [rsp + 16], 0
    je .Lsmp9_invalid
    lea rcx, [rip + _smp3094_ap_boot_state]
    mov rax, rbx
    shl rax, 6
    add rcx, rax
    cmp dword ptr [rcx + 52], 1  // AP IPI_ARMED
    jne .Lsmp9_invalid
    lea rcx, [rip + _smp9_timer_state]
    mov rax, rbx
    shl rax, 6
    add rcx, rax
    cmp r14d, 1
    jne .Lsmp9_set_command
    mov r8d, dword ptr [rsp]
    mov dword ptr [rip + _smp9_timer_initial_count], r8d
    mov qword ptr [rcx + 8], 0
.Lsmp9_set_command:
    mov dword ptr [rcx + 4], 0
    mov eax, r14d
    xchg dword ptr [rcx + 0], eax
    mfence
    rdtsc
    shl rdx, 32
    or rax, rdx
    mov r15, rax

.Lsmp9_icr_wait:
    mov rcx, qword ptr [rip + _smp6_lapic_base]
    test rcx, rcx
    jz .Lsmp9_invalid
    mov eax, dword ptr [rcx + 0x300]
    test eax, 0x1000
    jz .Lsmp9_icr_send
    pause
    rdtsc
    shl rdx, 32
    or rax, rdx
    sub rax, r15
    cmp rax, qword ptr [rsp + 8]
    jae .Lsmp9_timeout
    jmp .Lsmp9_icr_wait

.Lsmp9_icr_send:
    mov r8, qword ptr [rsp + 16]
    mov eax, dword ptr [r8 + 4]
    shl eax, 24
    mov dword ptr [rcx + 0x310], eax
    mov dword ptr [rcx + 0x300], 0x000000F1

.Lsmp9_wait_ack:
    lea rcx, [rip + _smp9_timer_state]
    mov rax, rbx
    shl rax, 6
    add rcx, rax
    cmp dword ptr [rcx + 4], r14d
    je .Lsmp9_success
    pause
    rdtsc
    shl rdx, 32
    or rax, rdx
    sub rax, r15
    cmp rax, qword ptr [rsp + 8]
    jae .Lsmp9_timeout
    jmp .Lsmp9_wait_ack

.Lsmp9_success:
    mov eax, 1
    jmp .Lsmp9_return
.Lsmp9_timeout:
    mov eax, -1
    jmp .Lsmp9_return
.Lsmp9_invalid:
    mov eax, -2
.Lsmp9_return:
    add rsp, 32
    pop r15
    pop r14
    pop r13
    pop r12
    pop rbx
    ret

// Wait for >=N real F3 AP-local IRQ deliveries. rdi=CPU ID,
// rsi=minimum ticks, rdx=global timeout cycles. Returns 1/-1/-2.
.global _native_x64_timer_wait_ticks
_native_x64_timer_wait_ticks:
    test edi, edi
    jz .Lsmp9_wait_bad
    cmp edi, 255
    ja .Lsmp9_wait_bad
    test esi, esi
    jz .Lsmp9_wait_bad
    test rdx, rdx
    jz .Lsmp9_wait_bad
    lea r8, [rip + _smp9_timer_state]
    mov eax, edi
    shl rax, 6
    add r8, rax
    mov r9, rdx
    mov r10, rsi
    rdtsc
    shl rdx, 32
    or rax, rdx
    mov r11, rax
.Lsmp9_tick_poll:
    cmp qword ptr [r8 + 8], r10
    jae .Lsmp9_wait_good
    pause
    rdtsc
    shl rdx, 32
    or rax, rdx
    sub rax, r11
    cmp rax, r9
    jb .Lsmp9_tick_poll
    mov eax, -1
    ret
.Lsmp9_wait_good:
    mov eax, 1
    ret
.Lsmp9_wait_bad:
    mov eax, -2
    ret

// After the stop ACK, allow at most one already pending F3 interrupt
// during a 30ms quiet window. No further periodic ticks are permitted.
.global _native_x64_timer_wait_quiet
_native_x64_timer_wait_quiet:
    test edi, edi
    jz .Lsmp9_quiet_bad
    cmp edi, 255
    ja .Lsmp9_quiet_bad
    test rsi, rsi
    jz .Lsmp9_quiet_bad
    lea r8, [rip + _smp9_timer_state]
    mov eax, edi
    shl rax, 6
    add r8, rax
    mov r9, qword ptr [r8 + 8]
    mov r10, rsi
    rdtsc
    shl rdx, 32
    or rax, rdx
    mov r11, rax
.Lsmp9_quiet_loop:
    pause
    rdtsc
    shl rdx, 32
    or rax, rdx
    sub rax, r11
    cmp rax, r10
    jb .Lsmp9_quiet_loop
    mov rax, qword ptr [r8 + 8]
    cmp rax, r9
    jb .Lsmp9_quiet_bad
    sub rax, r9
    cmp rax, 1
    ja .Lsmp9_quiet_bad
    cmp dword ptr [r8 + 4], 2
    jne .Lsmp9_quiet_bad
    cmp dword ptr [r8 + 16], 0x100F3
    jne .Lsmp9_quiet_bad
    mov eax, 1
    ret
.Lsmp9_quiet_bad:
    mov eax, -2
    ret

.macro SMP9_TIMER_GETTER name, offset
.global \name
\name:
    test edi, edi
    jz .Lsmp9_get_bad_\@
    cmp edi, 255
    ja .Lsmp9_get_bad_\@
    lea rcx, [rip + _smp9_timer_state]
    mov eax, edi
    shl rax, 6
    add rcx, rax
    mov rax, qword ptr [rcx + \offset]
    ret
.Lsmp9_get_bad_\@:
    xor eax, eax
    ret
.endm

SMP9_TIMER_GETTER _native_x64_get_ap_periodic_ticks, 8
SMP9_TIMER_GETTER _native_x64_get_ap_timer_lvt_after_stop, 16

'''
    text = replace_once(text, before_rendezvous, commands + before_rendezvous, path)

    bss = r'''_smp8_release_generation: .quad 0'''
    new_bss = r'''_smp8_release_generation: .quad 0

// Stage9b: independent 64-byte cache-line state per CPU (0..255).
// +0 command, +4 acknowledgement, +8 real F3 ticks, +16 last LVT.
// Timers are masked after every successful test.
.balign 64
_smp9_timer_state: .zero 16384
.balign 8
_smp9_timer_initial_count: .long 0
'''
    return replace_once(text, bss, new_bss, path)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("cosmos_root", type=Path)
    args = parser.parse_args()
    root = args.cosmos_root.resolve()
    files = {
        "src/Cosmos.Kernel.Core.X64/Cpu/SmpApBootstrap.cs": patch_core,
        "src/Cosmos.Kernel.HAL.X64/X64PlatformInitializer.cs": patch_hal,
        "src/Cosmos.Kernel.Native.X64/CPU/Interrupts.s": patch_asm,
    }
    changed = {}
    for relative, function in files.items():
        path = root / relative
        if not path.is_file():
            raise SystemExit(f"[SMP3094-STAGE9B][FAIL] missing {path}")
        original = path.read_text()
        updated = function(original, path)
        if original == updated:
            raise SystemExit(f"[SMP3094-STAGE9B][FAIL] unchanged {path}")
        changed[path] = updated

    # Make every preflight validation pass BEFORE changing any file. A
    # failed source ABI check must never leave a half-applied Cosmos tree.
    for path, content in changed.items():
        path.write_text(content)
        print(f"[SMP3094-STAGE9B] patched {path.relative_to(root)}")
    print("[SMP3094-STAGE9B] Native AP periodic-timer port installed (NOT managed SMP)")


if __name__ == "__main__":
    main()
