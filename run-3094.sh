#!/usr/bin/env bash
# A 3.0.94-only launcher for the isolated, read-only USB diagnostic ISO.
# Never invokes the legacy 3.0.85 run.sh, never writes a physical disk.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="dwierzbicki11/ZOnderqOS"
WORKFLOW="experimental-cosmos-3094-usb.yml"
ARTIFACT="zonderq-3094-usb-input-EXPERIMENTAL"
OUT="$ROOT/output-3094-usb"

die() { printf '[3094][ERROR] %s\n' "$*" >&2; exit 1; }
help() {
    cat <<'EOF'
ZonderqOS / Cosmos 3.0.94 -- safe USB diagnostic runner
  bash run-3094.sh --check      Verify pinned SDK, branch and workspace
  bash run-3094.sh --download   Download ISO built by GitHub Actions for exact HEAD
  bash run-3094.sh --qemu       Download and boot 1-vCPU QEMU with USB HID mouse
  bash run-3094.sh --help       Show usage

This is the read-only input probe (inputscan | mouse | help), NOT full
SMP/OrionGC/GUI production. It does NOT install or write to any physical disk.
EOF
}
preflight() {
    grep -Fq 'Sdk="Cosmos.Sdk/3.0.94"' "$ROOT/ZonderqOS.csproj" ||
        die "SDK must be Cosmos 3.0.94 -- refusing mixed versions."
    grep -Fq 'Version="3.0.94"' "$ROOT/ZonderqOS.csproj" ||
        die "Runtime must be Cosmos 3.0.94."
    grep -Fq '<ZonderqReadOnlyHardwareProbe Condition=' "$ROOT/ZonderqOS.csproj" ||
        die "Read-only hardware probe is not configured."
    command -v git >/dev/null || die "git not found"
    [[ -z "$(git -C "$ROOT" status --porcelain --untracked-files=no)" ]] ||
        die "Working tree has modified tracked files; commit/stash first."
    BRANCH="$(git -C "$ROOT" branch --show-current)"
    [[ -n "$BRANCH" ]] || die "Named branch required"
    SHA="$(git -C "$ROOT" rev-parse HEAD)"
    echo "[3094] Verified SDK/runtime v3.0.94, branch=$BRANCH, commit=${SHA:0:12}"
}
download_artifact() {
    command -v gh >/dev/null || die "GitHub CLI (gh) required. Use the Actions webpage to download manually."
    command -v jq >/dev/null || die "jq required to validate workflow commit."
    local runs run_id temp
    # Use workflow display-name filtering, not --workflow filename: the
    # experimental workflow has not reached the default branch yet.
    runs="$(gh run list --repo "$REPO" --branch "$BRANCH" --limit 100 \
        --json databaseId,headSha,conclusion,name)" ||
        die "Cannot obtain GitHub Actions runs (check gh auth status)."
    run_id="$(printf '%s' "$runs" | jq -r --arg sha "$SHA" \
        '[.[] | select(.name == "Experimental ZonderqOS Cosmos 3.0.94 USB input" and .conclusion == "success" and .headSha == $sha)] | .[0].databaseId // empty')"
    [[ -n "$run_id" ]] ||
        die "No successful 3.0.94 workflow for exact HEAD. Review GitHub Actions; cannot use stale ISO."
    temp="$(mktemp -d)"
    trap 'rm -rf -- "$temp"' RETURN
    gh run download "$run_id" --repo "$REPO" --name "$ARTIFACT" --dir "$temp" ||
        die "Artifact download failed or artifact expired."
    [[ -s "$temp/ZonderqOS.iso" ]] || die "Missing nonempty ISO."
    mkdir -p "$OUT"
    cp -- "$temp/ZonderqOS.iso" "$OUT/ZonderqOS.iso"
    printf '[3094] ISO: %s\n' "$OUT/ZonderqOS.iso"
    sha256sum "$OUT/ZonderqOS.iso"
}
case "${1:---help}" in
    --help|-h) help ;;
    --check) preflight ;;
    --download) preflight; download_artifact ;;
    --qemu)
        preflight
        download_artifact
        command -v qemu-system-x86_64 >/dev/null || die "Install qemu-system-x86."
        qemu-system-x86_64 \
            -machine q35 -cpu Nehalem -smp 1 -m 2048M \
            -cdrom "$OUT/ZonderqOS.iso" -boot d \
            -device qemu-xhci,id=xhci -device usb-mouse,bus=xhci.0 \
            -serial stdio -no-reboot
        ;;
    *) die "Unknown option: $1 (see --help)" ;;
esac
