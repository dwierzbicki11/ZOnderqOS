#!/usr/bin/env bash
set -euo pipefail

ISO_PATH="${1:-output-x64/ZonderqOS.iso}"
LOG_PATH="${2:-stage01-qemu-serial.log}"
CPU_MODEL="${STAGE01_QEMU_CPU_MODEL:-Nehalem}"
SOCKETS="${STAGE01_QEMU_SOCKETS:-1}"
CPUS="${STAGE01_QEMU_CPUS:-8}"
CORES="${STAGE01_QEMU_CORES:-4}"
THREADS="${STAGE01_QEMU_THREADS:-2}"
TIMEOUT_SECONDS="${STAGE01_QEMU_TIMEOUT_SECONDS:-90}"

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

for value_name in SOCKETS CPUS CORES THREADS TIMEOUT_SECONDS; do
    value="${!value_name}"
    [[ "$value" =~ ^[1-9][0-9]*$ ]] || fail "$value_name musi byc dodatnia liczba calkowita (otrzymano: $value)."
done

expected_cpus=$((SOCKETS * CORES * THREADS))
[[ "$CPUS" -eq "$expected_cpus" ]] || \
    fail "Nieprawidlowa topologia: cpus=$CPUS, ale sockets*cores*threads=${SOCKETS}*${CORES}*${THREADS}=$expected_cpus."

qemu-system-x86_64 -cpu help 2>/dev/null | grep -Eq "(^|[[:space:]])${CPU_MODEL}([[:space:]]|$)" || \
    fail "Model CPU QEMU '$CPU_MODEL' nie jest dostepny na tym hoście."

: > "$LOG_PATH"

echo "[SMT-QEMU] Model CPU: $CPU_MODEL"
echo "[SMT-QEMU] Topologia: ${SOCKETS}S/${CORES}C/${THREADS}T = ${CPUS} logicznych CPU"

qemu-system-x86_64 \
    -M q35 \
    -cpu "$CPU_MODEL" \
    -smp "cpus=$CPUS,sockets=$SOCKETS,cores=$CORES,threads=$THREADS" \
    -m 2G \
    -drive "file=$ISO_PATH,media=cdrom,if=ide,readonly=on" -boot d \
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
    if grep -Eq 'CPU EXCEPTION|System halted|Kernel Panic|\bPANIC\b|Unhandled Exception|Triple fault' "$LOG_PATH"; then
        fail "Kernel zgłosil fatalny blad podczas bootu."
    fi

    if grep -Fq '[SMP] WARNING:' "$LOG_PATH"; then
        fail "Diagnostyka SMP Etapu 1 zglosila warning."
    fi

    if grep -Fq '[SMP] Stage 1 complete: MP response inspected; APs remain parked.' "$LOG_PATH"; then
        grep -Fq "[SMP] Limine MP response: $CPUS logical CPU(s)" "$LOG_PATH" || \
            fail "Etap 1 skonczyl sie, ale Limine nie zglosil oczekiwanych $CPUS logicznych CPU."

        cpu_lines="$(grep -Fc '[SMP] CPU[' "$LOG_PATH" || true)"
        [[ "$cpu_lines" -eq "$CPUS" ]] || \
            fail "Oczekiwano $CPUS deskryptorow CPU, znaleziono: $cpu_lines."

        echo "[SMT-QEMU][OK] ZonderqOS bootuje w QEMU ${SOCKETS}S/${CORES}C/${THREADS}T."
        echo "[SMT-QEMU][OK] Limine przekazal $CPUS logicznych CPU, BSP zostal rozpoznany, AP-y pozostaly zaparkowane."
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
