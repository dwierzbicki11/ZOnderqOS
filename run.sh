#!/usr/bin/env bash
# Production-track launcher: stock upstream Cosmos 3.0.95, BSP-only QEMU.
# Never patches Cosmos, never invokes the archived Stage-23 builder.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$ROOT/output-3095"
PROBE="$OUT/ZonderqOS-3095-usb.iso"

fail() { printf '[ZONDERQ][ERROR] %s\n' "$*" >&2; exit 1; }
check() {
  bash "$ROOT/tools/verify-cosmos-compat.sh"
  [[ -z "$(git -C "$ROOT" status --porcelain --untracked-files=no)" ]] ||
    fail "Uncommitted tracked changes; use a clean checkout for reproducible ISO."
  printf '[ZONDERQ] Stock Cosmos 3.0.95; no custom SMP. QEMU uses exactly 1 vCPU.\n'
}
help() {
  cat <<'EOF'
ZonderqOS Gen3 / upstream Cosmos 3.0.95 / custom SMP disabled

  ./run.sh --check         Validate version, upstream-only build and no SMP patches
  ./run.sh --build         Build full disk-backed GUI ISO (requires ZONDERQDATA)
  ./run.sh --build-probe   Build read-only diagnostic ISO
  ./run.sh --qemu          Boot existing read-only probe ISO in QEMU (1 vCPU)
  ./run.sh --help          This help

Only the stock Cosmos 3.0.95 runtime is used. No custom AP startup, Stage23,
SMT scheduler or 3.0.85/3.0.94 fallback. Do not write an ISO to a host disk.
EOF
}
case "${1:---help}" in
  --help|-h) help ;;
  --check|--status) check ;;
  --build) check; exec bash "$ROOT/tools/build-cosmos-3095-local.sh" disk ;;
  --build-probe) check; exec bash "$ROOT/tools/build-cosmos-3095-local.sh" probe ;;
  --qemu)
    check
    [[ -s "$PROBE" ]] || fail "Missing $PROBE. First run ./run.sh --build-probe."
    command -v qemu-system-x86_64 >/dev/null || fail "Install qemu-system-x86."
    exec qemu-system-x86_64 -machine q35 -cpu Nehalem -smp 1 -m 2048M \
      -cdrom "$PROBE" -boot d \
      -device qemu-xhci,id=xhci -device usb-mouse,bus=xhci.0 \
      -serial stdio -no-reboot
    ;;
  *) fail "Unknown option: $1. See ./run.sh --help." ;;
esac
