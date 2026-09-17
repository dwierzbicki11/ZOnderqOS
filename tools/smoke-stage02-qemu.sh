#!/usr/bin/env bash
set -euo pipefail

ISO_PATH="${1:-output-x64/ZonderqOS.iso}"
LOG_PATH="${2:-stage02-qemu-serial.log}"
TIMEOUT_SECONDS="${STAGE02_QEMU_TIMEOUT_SECONDS:-75}"

fail() {
    echo "[SMT-QEMU][FAIL] $*" >&2
    if [[ -f "$LOG_PATH" ]]; then
        echo "[SMT-QEMU] Ostatnie 200 linii seriala:" >&2
        tail -n 200 "$LOG_PATH" >&2 || true
    fi
    exit 1
}

command -v qemu-system-x86_64 >/dev/null 2>&1 || fail "Brak qemu-system-x86_64 w PATH."
[[ -f "$ISO_PATH" ]] || fail "Brak ISO: $ISO_PATH"
[[ "$TIMEOUT_SECONDS" =~ ^[1-9][0-9]*$ ]] || fail "Nieprawidlowy timeout: $TIMEOUT_SECONDS"

: > "$LOG_PATH"

qemu-system-x86_64 \
    -M q35 \
    -cpu qemu64 \
    -smp cpus=8,sockets=1,cores=4,threads=2 \
    -m 2G \
    -drive "file=$ISO_PATH,if=none,id=cosmoscd,format=raw,readonly=on" \
    -device ide-cd,drive=cosmoscd,bootindex=0 \
    -boot d \
    -display none \
    -monitor none \
    -serial "file:$LOG_PATH" \
    -no-reboot \
    -no-shutdown &
qemu_pid=$!

cleanup() {
    if kill -0 "$qemu_pid" >/dev/null 2>&1; then
        kill "$qemu_pid" >/dev/null 2>&1 || true
        wait "$qemu_pid" >/dev/null 2>&1 || true
    fi
}
trap cleanup EXIT

start_seconds=$SECONDS
while (( SECONDS - start_seconds < TIMEOUT_SECONDS )); do
    if grep -Eiq 'CPU EXCEPTION|System halted|Kernel Panic|\bPANIC\b|Unhandled Exception|Triple fault' "$LOG_PATH"; then
        fail "Kernel zgłosil fatalny blad podczas bootu."
    fi

    if grep -Fq '[SMP] WARNING:' "$LOG_PATH"; then
        fail "Diagnostyka SMP Etapu 2 zglosila warning."
    fi

    if grep -Fq '[SMP] Stage 2 complete: dense CPU topology ready; APs remain parked.' "$LOG_PATH"; then
        grep -Fq '[SMP] Stage 1 complete: MP response inspected; APs remain parked.' "$LOG_PATH" || \
            fail "Etap 2 zakonczyl sie bez markera Etapu 1."
        grep -Fq '[SMP] Limine MP response: 8 logical CPU(s)' "$LOG_PATH" || \
            fail "Limine nie zglosil oczekiwanych 8 logicznych CPU dla profilu 4C/8T."

        map_lines="$(grep -Fc '[SMP] MAP CpuId=' "$LOG_PATH" || true)"
        [[ "$map_lines" == "8" ]] || \
            fail "Oczekiwano 8 wpisow dense CPU map, znaleziono: $map_lines."

        for cpu_id in 0 1 2 3 4 5 6 7; do
            grep -Eq "\[SMP\] MAP CpuId=${cpu_id} LAPIC=[0-9]+" "$LOG_PATH" || \
                fail "Brak dense CpuId=${cpu_id} w mapie Etapu 2."
        done

        grep -Eq '\[SMP\] MAP CpuId=0 LAPIC=[0-9]+ BSP' "$LOG_PATH" || \
            fail "Dense CpuId=0 nie zostal przypisany do BSP."

        echo "[SMT-QEMU][OK] ZonderqOS bootuje w QEMU 4C/8T z Etapem 2."
        echo "[SMT-QEMU][OK] Osiem LAPIC ID zostalo zmapowanych na dense CpuId 0..7, BSP=CpuId0; AP-y nadal sa zaparkowane."
        tail -n 140 "$LOG_PATH" || true
        exit 0
    fi

    if ! kill -0 "$qemu_pid" >/dev/null 2>&1; then
        wait "$qemu_pid" || true
        fail "QEMU zakonczyl sie przed potwierdzeniem Etapu 2."
    fi

    sleep 1
done

fail "Timeout ${TIMEOUT_SECONDS}s bez potwierdzenia Etapu 2."
