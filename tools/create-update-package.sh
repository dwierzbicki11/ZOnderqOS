#!/usr/bin/env bash
# ZonderqOS Gen 3 — Real USB Update Package Creator
# Generates a cryptographically verified update package with real SHA-256 digests and HMAC signature.
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:-1.0.1}"
BUILD="${2:-3110}"
OUTPUT_DIR="${3:-$ROOT/output-3095/updates-package}"
KERNEL_BIN="${4:-$ROOT/obj/Debug/net10.0/linux-x64/cosmos/iso_root/boot/ZonderqOS.elf}"
SIGNER_KEY="${ZONDERQ_SIGNER_KEY:-ZONDERQ_OFFICIAL_RELEASE_KEY_GEN3}"

fail() { printf '[ZONDERQ-PKG][ERROR] %s\n' "$*" >&2; exit 1; }

for cmd in sha256sum openssl python3 mkdir cp stat; do
    command -v "$cmd" >/dev/null 2>&1 || fail "Brakuje narzedzia: $cmd"
done

[[ -f "$KERNEL_BIN" && -s "$KERNEL_BIN" ]] || fail "Nie znaleziono pliku kernela ELF: $KERNEL_BIN"

mkdir -p "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR/payload"

KERNEL_NAME="$(basename "$KERNEL_BIN")"
cp -f "$KERNEL_BIN" "$OUTPUT_DIR/$KERNEL_NAME"
KERNEL_SHA256="$(sha256sum "$OUTPUT_DIR/$KERNEL_NAME" | awk '{print $1}')"
KERNEL_SIZE="$(stat -c%s "$OUTPUT_DIR/$KERNEL_NAME")"

TITLE="ZonderqOS Gen3 $VERSION Kernel & System Update"
DATE="$(date +%Y-%m-%d)"
ARCH="x86_64"

# Generate canonical signature payload: Version|Build|Arch|KernelImage|KernelSha256|Title
SIG_PAYLOAD="$VERSION|$BUILD|$ARCH|$KERNEL_NAME|$KERNEL_SHA256|$TITLE"
SIGNATURE="$(python3 -c '
import hmac, hashlib, sys
key = sys.argv[1].encode("ascii")
msg = sys.argv[2].encode("ascii")
print(hmac.new(key, msg, hashlib.sha256).hexdigest())
' "$SIGNER_KEY" "$SIG_PAYLOAD")"

MANIFEST="$OUTPUT_DIR/update.manifest"
cat > "$MANIFEST" <<EOF
# ZonderqOS Verified Update Package Manifest
VERSION=$VERSION
BUILD=$BUILD
ARCH=$ARCH
TITLE=$TITLE
DATE=$DATE
SEVERITY=Zalecana
KERNEL_IMAGE=$KERNEL_NAME
KERNEL_SHA256=$KERNEL_SHA256
PAYLOAD_DIR=payload
SIGNATURE=$SIGNATURE
THEME=EMERALD FOREST
WALLPAPER=6
CHANGELOG=Aktualizacja jadra Cosmos 3.0.95 x64 Native Core;Obsluga bezpiecznego przelaczania slotow rozruchowych A/B;Automatyczny watchdog awaryjny po nieudanym starcie;Weryfikacja kryptograficzna SHA-256 i HMAC
COMPONENTS=Cosmos 3.0.95 x64 Native Core;ZonderqOS Kernel ELF;BootSlotManager A/B;IntegrityGuard 2.4
EOF

printf '\n[ZONDERQ-PKG] Pomyslnie wygenerowano pakiet aktualizacji USB!\n'
printf '  Katalog:        %s\n' "$OUTPUT_DIR"
printf '  Wersja celu:    %s (Build %s)\n' "$VERSION" "$BUILD"
printf '  Architektura:   %s\n' "$ARCH"
printf '  Kernel ELF:     %s (%s bajtow)\n' "$KERNEL_NAME" "$KERNEL_SIZE"
printf '  SHA-256 kernel: %s\n' "$KERNEL_SHA256"
printf '  Podpis HMAC:    %s\n' "$SIGNATURE"
printf '  Plik manifestu: %s\n\n' "$MANIFEST"
