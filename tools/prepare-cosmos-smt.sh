#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PATCH_FILE="$ROOT_DIR/patches/cosmos-smt/0001-limine-mp-request.patch"
COSMOS_ROOT="${ZONDERQ_COSMOS_SOURCE_ROOT:-$(cd "$ROOT_DIR/.." && pwd)}"
PATCH_ONLY=0

if [[ "${1:-}" == "--patch-only" ]]; then
    PATCH_ONLY=1
elif [[ $# -gt 0 ]]; then
    echo "Uzycie: $0 [--patch-only]" >&2
    exit 2
fi

fail() {
    echo "[SMT][BLAD] $*" >&2
    exit 1
}

[[ -f "$PATCH_FILE" ]] || fail "Brak patcha: $PATCH_FILE"
[[ -d "$COSMOS_ROOT/.git" ]] || fail "COSMOS_ROOT nie jest repozytorium git: $COSMOS_ROOT"
[[ -f "$COSMOS_ROOT/src/Cosmos.Kernel.Core/Scheduler/SchedulerManager.cs" ]] || \
    fail "To nie wyglada na checkout Cosmos Gen3: $COSMOS_ROOT"
[[ -f "$COSMOS_ROOT/src/Cosmos.Kernel.Native.X64/CPU/Interrupts.s" ]] || \
    fail "Brak natywnego backendu x64 w: $COSMOS_ROOT"

if ! git -C "$COSMOS_ROOT" diff --quiet || ! git -C "$COSMOS_ROOT" diff --cached --quiet; then
    fail "Checkout Cosmosa ma lokalne zmiany. Commit/stash je przed nakladaniem patchy SMT."
fi

echo "[SMT] Cosmos source: $COSMOS_ROOT"
echo "[SMT] Patch series: $PATCH_FILE"

if git -C "$COSMOS_ROOT" apply --reverse --check "$PATCH_FILE" >/dev/null 2>&1; then
    echo "[SMT] Patch Limine MP jest juz zastosowany."
else
    git -C "$COSMOS_ROOT" apply --check "$PATCH_FILE" || \
        fail "Patch nie pasuje do tego checkoutu Cosmosa. Uzyj gen3/v3.0.85 zgodnego z ZonderqOS."
    git -C "$COSMOS_ROOT" apply "$PATCH_FILE"
    echo "[SMT] Zastosowano etap 1: Limine MP request."
fi

if (( PATCH_ONLY )); then
    echo "[SMT] Patch-only: pomijam budowanie paczek."
    exit 0
fi

[[ -x "$COSMOS_ROOT/.devcontainer/postCreateCommand.sh" || -f "$COSMOS_ROOT/.devcontainer/postCreateCommand.sh" ]] || \
    fail "Brak oficjalnego skryptu budowania paczek Cosmosa."

cat <<'EOF'
[SMT] Buduje lokalne paczki Cosmos 3.0.85 z patchem.
[SMT] Oficjalny skrypt Cosmosa wyczysci cache cosmos.*, utworzy lokalny feed
[SMT] i zainstaluje lokalne narzedzia Cosmos.Patcher/Cosmos.Tools.
EOF

(
    cd "$COSMOS_ROOT"
    export VersionPrefix=3.0.85
    bash .devcontainer/postCreateCommand.sh
)

echo
 echo "[SMT] Lokalne paczki gotowe."
 echo "[SMT] Etap 1 tylko przygotowuje i parkuje AP-y przez Limine."
 echo "[SMT] Nie uruchamia jeszcze managed scheduler/GC na dodatkowych CPU."
 echo "[SMT] Teraz ./run.sh x64 uzyje lokalnego cache/feed Cosmosa 3.0.85."
