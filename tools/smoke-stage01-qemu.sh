#!/usr/bin/env bash
set -euo pipefail

ISO_PATH="${1:-output-x64/ZonderqOS.iso}"
LOG_PATH="${2:-stage01-qemu-serial.log}"
TIMEOUT_SECONDS="${STAGE01_QEMU_TIMEOUT_SECONDS:-75}"

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
        fail "Diagnostyka SMP Etapu 1 zglosila warning."
    fi

    if grep -Fq '[SMP] Stage 1 complete: MP response inspected; APs remain parked.' "$LOG_PATH"; then
        grep -Fq '[SMP] Limine MP response: 8 logical CPU(s)' "$LOG_PATH" || \
            fail "Etap 1 skonczyl sie, ale Limine nie zglosil oczekiwanych 8 logicznych CPU dla profilu 4C/8T."

        cpu_lines="$(grep -Fc '[SMP] CPU[' "$LOG_PATH" || true)"
        [[ "$cpu_lines" == "8" ]] || \
            fail "Oczekiwano 8 deskryptorow CPU, znaleziono: $cpu_lines."

        echo "[SMT-QEMU][OK] ZonderqOS bootuje w QEMU 4C/8T."
        echo "[SMT-QEMU][OK] Limine przekazal 8 logicznych CPU, BSP zostal rozpoznany, AP-y pozostaly zaparkowane."
        tail -n 120 "$LOG_PATH" || true
        exit 0
    fi

    if ! kill -0 "$qemu_pid" >/dev/null 2>&1; then
        wait "$qemu_pid" || true
        fail "QEMU zakonczyl sie przed potwierdzeniem Etapu 1."
    fi

    sleep 1
done

fail "Timeout ${TIMEOUT_SECONDS}s bez potwierdzenia Etapu 1."
