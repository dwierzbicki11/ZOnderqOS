#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PATCH_DIR="$ROOT_DIR/patches/cosmos-smt"
COSMOS_ROOT="${ZONDERQ_COSMOS_SOURCE_ROOT:-$(cd "$ROOT_DIR/.." && pwd)}"
PATCH_ONLY=0

if [[ "${1:-}" == "--patch-only" ]]; then
    PATCH_ONLY=1
elif [[ $# -gt 0 ]]; then
    echo "Uzycie: bash $0 [--patch-only]" >&2
    exit 2
fi

fail() {
    echo "[SMT][BLAD] $*" >&2
    exit 1
}

[[ -d "$PATCH_DIR" ]] || fail "Brak katalogu patchy: $PATCH_DIR"
[[ -d "$COSMOS_ROOT/.git" ]] || fail "COSMOS_ROOT nie jest repozytorium git: $COSMOS_ROOT"
[[ -f "$COSMOS_ROOT/src/Cosmos.Kernel.Core/Scheduler/SchedulerManager.cs" ]] || \
    fail "To nie wyglada na checkout Cosmos Gen3: $COSMOS_ROOT"
[[ -f "$COSMOS_ROOT/src/Cosmos.Kernel.Native.X64/CPU/Interrupts.s" ]] || \
    fail "Brak natywnego backendu x64 w: $COSMOS_ROOT"

mapfile -t PATCHES < <(find "$PATCH_DIR" -maxdepth 1 -type f -name '*.patch' | sort)
(( ${#PATCHES[@]} > 0 )) || fail "Brak plikow *.patch w $PATCH_DIR"

# Patches intentionally leave the Cosmos checkout dirty. Staged changes are
# never ours, though, so refuse to run if the user has anything in the index.
if ! git -C "$COSMOS_ROOT" diff --cached --quiet; then
    fail "Checkout Cosmosa ma staged changes. Commit/stash je przed SMT bring-up."
fi

echo "[SMT] Cosmos source: $COSMOS_ROOT"
echo "[SMT] Patch series: ${#PATCHES[@]} etap(y)"

applied_count=0
pending_count=0
for patch in "${PATCHES[@]}"; do
    name="$(basename "$patch")"
    if git -C "$COSMOS_ROOT" apply --reverse --check "$patch" >/dev/null 2>&1; then
        echo "[SMT] OK (juz jest): $name"
        applied_count=$((applied_count + 1))
    elif git -C "$COSMOS_ROOT" apply --check "$patch" >/dev/null 2>&1; then
        echo "[SMT] DO ZASTOSOWANIA: $name"
        pending_count=$((pending_count + 1))
    else
        fail "Patch '$name' ani nie jest zastosowany, ani nie pasuje do checkoutu. Uzyj zgodnego Cosmos gen3/v3.0.85 albo wyczysc obce zmiany."
    fi
done

# A completely fresh patch run must start from a clean source tree. On later
# runs unstaged changes are expected because git apply intentionally does not
# commit the SMT series into the upstream checkout.
if (( applied_count == 0 )) && ! git -C "$COSMOS_ROOT" diff --quiet; then
    fail "Checkout Cosmosa ma obce lokalne zmiany. Commit/stash je przed pierwszym zastosowaniem patchy SMT."
fi

if (( applied_count > 0 )) && ! git -C "$COSMOS_ROOT" diff --quiet; then
    echo "[SMT] Checkout jest dirty (oczekiwane po git apply)."
fi

for patch in "${PATCHES[@]}"; do
    name="$(basename "$patch")"
    if git -C "$COSMOS_ROOT" apply --reverse --check "$patch" >/dev/null 2>&1; then
        continue
    fi
    git -C "$COSMOS_ROOT" apply --check "$patch" || fail "Nie mozna zastosowac: $name"
    git -C "$COSMOS_ROOT" apply "$patch"
    echo "[SMT] Zastosowano: $name"
done

if (( PATCH_ONLY )); then
    echo "[SMT] Patch-only: pomijam budowanie paczek."
    exit 0
fi

[[ -f "$COSMOS_ROOT/.devcontainer/postCreateCommand.sh" ]] || \
    fail "Brak oficjalnego skryptu budowania paczek Cosmosa."

cat <<'EOF'
[SMT] Buduje lokalne paczki Cosmos 3.0.85 z seria SMT.
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
echo "[SMT] Obecny etap: Limine przygotowuje/parkuje AP-y, a x64 ma per-CPU context-switch staging."
echo "[SMT] AP-y NIE sa jeszcze wypuszczane do managed scheduler/GC — to celowe, dopoki nie ma GC stop-the-world."
echo "[SMT] ZonderqOS moze teraz budowac sie przeciw lokalnym paczkom 3.0.85."
