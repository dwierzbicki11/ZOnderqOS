#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT_DIR"

fail() {
    echo "[VERIFY][FAIL] $*" >&2
    exit 1
}

command -v cosmos >/dev/null 2>&1 || fail "Brak 'cosmos' w PATH."

build_arch() {
    local arch="$1"
    local label
    label="$(printf '%s' "$arch" | tr '[:lower:]' '[:upper:]')"

    echo
    echo "========================================"
    echo "[VERIFY] $label clean build"
    echo "========================================"

    # NativeAOT/MSBuild intermediates are shared by architecture. Always clean
    # between targets so the verification catches source/API issues rather than
    # accidentally reusing the other architecture's resolved assemblies.
    rm -rf "$ROOT_DIR/bin" "$ROOT_DIR/obj" "$ROOT_DIR/output-$arch"
    cosmos build -a "$arch"

    local iso="$ROOT_DIR/output-$arch/ZonderqOS.iso"
    [[ -f "$iso" ]] || fail "$label build skonczyl sie bez $iso"

    echo "[VERIFY][OK] $label -> $iso"
}

build_arch x64
build_arch arm64

echo
echo "[VERIFY][OK] ZonderqOS zbudowal sie clean dla x64 i ARM64."
