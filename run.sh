#!/usr/bin/env bash
# Production-track launcher: stock upstream Cosmos 3.0.95, BSP-only QEMU.
# Never patches Cosmos, never invokes the archived Stage-23 builder.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$ROOT/output-3095"
PROBE="$OUT/ZonderqOS-3095-usb.iso"
AUDIO_ISO="$OUT/ZonderqOS-3095-audio.iso"
AUDIO_WAV="${ZONDERQ_AUDIO_WAV:-$OUT/audio-capture.wav}"

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
  ./run.sh --build-audio   Build isolated stock HDA/PCM probe ISO (no disk mounts)
  ./run.sh --qemu          Boot existing read-only probe ISO in QEMU (1 vCPU)
  ./run.sh --qemu-audio    Boot audio ISO with Intel HDA + WAV capture (1 vCPU)
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
  --build-audio) check; exec bash "$ROOT/tools/build-cosmos-3095-local.sh" audio ;;
  --qemu-audio)
    check
    [[ -s "$AUDIO_ISO" ]] || fail "Missing $AUDIO_ISO. First run ./run.sh --build-audio."
    command -v qemu-system-x86_64 >/dev/null || fail "Install qemu-system-x86."
    mkdir -p "$OUT"
    echo "[AUDIO] QEMU Intel HDA, hda-duplex; capture: $AUDIO_WAV"
    echo "[AUDIO] At audio probe console: audioinfo, then tone. After exit:"
    echo "[AUDIO] python3 tools/verify-audio-wav-capture.py '$AUDIO_WAV'"
    exec qemu-system-x86_64 -machine q35 -cpu Nehalem -smp 1 -m 2048M \
      -cdrom "$AUDIO_ISO" -boot d -nic none \
      -device qemu-xhci,id=xhci -device usb-mouse,bus=xhci.0 \
      -audiodev "wav,id=audio0,path=$AUDIO_WAV" \
      -device intel-hda -device hda-duplex,audiodev=audio0 \
      -serial stdio -no-reboot
    ;;
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
