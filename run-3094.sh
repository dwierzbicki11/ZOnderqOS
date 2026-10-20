#!/usr/bin/env bash
# Cosmos 3.0.94 ONLY. Never run legacy 3.0.85 builder or write disks.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="dwierzbicki11/ZOnderqOS"
WORKFLOW="Experimental ZonderqOS Cosmos 3.0.94 USB input"
OUT="$ROOT/output-3094"
PROBE_ARTIFACT="zonderq-3094-usb-input-EXPERIMENTAL"
DISK_ARTIFACT="zonderq-3094-NORMAL-GUI-REQUIRES-ZONDERQDATA"

die() { printf '[3094][ERROR] %s\n' "$*" >&2; exit 1; }

help() {
    cat <<'EOF'
ZonderqOS Cosmos 3.0.94
  bash run.sh --check            Verify pinned project, workspace and 3.0.94
  bash run.sh --download         Download read-only USB diagnostic ISO
  bash run.sh --download-disk    Download persistent full GUI ISO
  bash run.sh --qemu             Run read-only USB probe in QEMU (1 CPU)
  bash run.sh --help             Show this help

The persistent GUI image requires a dedicated FAT32 ZONDERQDATA partition,
and is NOT a disk installer. NEVER format Kali's EFI or root partitions.
Full managed SMP/OrionGC Stage23 is not yet certified on Cosmos 3.0.94.
EOF
}
preflight() {
    grep -Fq 'Sdk="Cosmos.Sdk/3.0.94"' "$ROOT/ZonderqOS.csproj" ||
        die "SDK must be Cosmos 3.0.94."
    grep -Fq 'Version="3.0.94"' "$ROOT/ZonderqOS.csproj" ||
        die "Runtime must be Cosmos 3.0.94."
    grep -Fq '<ZonderqReadOnlyHardwareProbe Condition=' "$ROOT/ZonderqOS.csproj" ||
        die "Read-only input probe is missing."
    command -v git >/dev/null || die "git is required"
    [[ -z "$(git -C "$ROOT" status --porcelain --untracked-files=no)" ]] ||
        die "Uncommitted tracked files: commit/stash before downloading a pinned image."
    BRANCH="$(git -C "$ROOT" branch --show-current)"
    [[ -n "$BRANCH" ]] || die "A named git branch is required"
    SHA="$(git -C "$ROOT" rev-parse HEAD)"
    echo "[3094] Pinned SDK/runtime v3.0.94, branch=$BRANCH, commit=${SHA:0:12}"
}
download_artifact() {
    local artifact="$1" output_name="$2" runs run_id temp
    command -v gh >/dev/null || die "Install gh CLI or download via GitHub Actions Artifacts browser."
    command -v jq >/dev/null || die "Install jq to verify exact-commit provenance."
    runs="$(gh run list --repo "$REPO" --branch "$BRANCH" --limit 100 \
        --json databaseId,headSha,conclusion,name)" ||
        die "Cannot get GitHub workflow runs (check gh auth)."
    run_id="$(printf '%s' "$runs" | jq -r --arg sha "$SHA" --arg workflow "$WORKFLOW" \
        '[.[] | select(.headSha == $sha and .conclusion == "success" and .name == $workflow)] | .[0].databaseId // empty')"
    [[ -n "$run_id" ]] ||
        die "No successful ISOLATED Cosmos 3.0.94 workflow for exact commit. Refusing stale ISO."
    temp="$(mktemp -d)"
    trap 'rm -rf -- "$temp"' RETURN
    gh run download "$run_id" --repo "$REPO" --name "$artifact" --dir "$temp" ||
        die "ISO artifact unavailable or expired."
    [[ -s "$temp/ZonderqOS.iso" ]] || die "No valid ISO in artifact."
    mkdir -p "$OUT"
    cp -- "$temp/ZonderqOS.iso" "$OUT/$output_name"
    printf '[3094] ISO: %s\n' "$OUT/$output_name"
    sha256sum "$OUT/$output_name"
}
case "${1:---help}" in
    --help|-h) help ;;
    --check) preflight ;;
    --download)
        preflight
        download_artifact "$PROBE_ARTIFACT" "ZonderqOS-3094-usb.iso"
        ;;
    --download-disk)
        preflight
        download_artifact "$DISK_ARTIFACT" "ZonderqOS-3094-disk.iso"
        ;;
    --qemu)
        preflight
        download_artifact "$PROBE_ARTIFACT" "ZonderqOS-3094-usb.iso"
        command -v qemu-system-x86_64 >/dev/null || die "Install qemu-system-x86."
        qemu-system-x86_64 \
            -machine q35 -cpu Nehalem -smp 1 -m 2048M \
            -cdrom "$OUT/ZonderqOS-3094-usb.iso" -boot d \
            -device qemu-xhci,id=xhci -device usb-mouse,bus=xhci.0 \
            -serial stdio -no-reboot
        ;;
    *) die "Unknown option: $1 (see --help)" ;;
esac
