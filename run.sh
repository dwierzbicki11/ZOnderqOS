#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COSMOS_TAG="v3.0.85"
COSMOS_REPO="https://github.com/CosmosOS/Cosmos.git"

fail() {
    echo "[BLAD] $*" >&2
    exit 1
}

cleanup_generated_tracked_changes() {
    local dirty
    local path
    local unsafe=0
    local -a generated=()

    dirty="$(git -C "$ROOT_DIR" status --porcelain --untracked-files=no)"
    [[ -n "$dirty" ]] || return 0

    while IFS= read -r line; do
        [[ -n "$line" ]] || continue
        path="${line:3}"
        case "$path" in
            output-*/*|bin/*|obj/*|.nuget/*)
                generated+=("$path")
                ;;
            *)
                unsafe=1
                ;;
        esac
    done <<< "$dirty"

    if (( ${#generated[@]} > 0 )); then
        echo "[GIT] Cofam lokalne zmiany tylko w wygenerowanych artefaktach builda..."
        git -C "$ROOT_DIR" restore --worktree --staged -- "${generated[@]}" 2>/dev/null || \
            git -C "$ROOT_DIR" checkout -- "${generated[@]}"
    fi

    if (( unsafe )); then
        echo "[BLAD] Sa lokalne zmiany w prawdziwych plikach projektu; nie bede ich automatycznie kasowal." >&2
        git -C "$ROOT_DIR" status --short >&2
        exit 1
    fi
}

is_cosmos_checkout() {
    local path="$1"
    [[ -d "$path" ]] || return 1
    git -C "$path" rev-parse --is-inside-work-tree >/dev/null 2>&1 || return 1
    [[ -f "$path/src/Cosmos.Kernel.Core/Runtime/Stdllib.cs" ]] || return 1
}

bootstrap_cosmos_checkout() {
    local target="$1"

    command -v git >/dev/null 2>&1 || \
        fail "Brak git w PATH, a checkout Cosmos nie istnieje."

    if [[ -e "$target" ]]; then
        fail "Nie znaleziono poprawnego checkoutu Cosmos, a sciezka docelowa juz istnieje: $target"
    fi

    echo "[SMT] Nie znaleziono Cosmos. Klonuje $COSMOS_TAG do: $target"
    git clone --recursive --branch "$COSMOS_TAG" "$COSMOS_REPO" "$target"

    is_cosmos_checkout "$target" || \
        fail "Sklonowano Cosmos, ale checkout jest niekompletny: $target"
}

resolve_cosmos_root() {
    if [[ -n "${ZONDERQ_COSMOS_SOURCE_ROOT:-}" ]]; then
        is_cosmos_checkout "$ZONDERQ_COSMOS_SOURCE_ROOT" || \
            fail "ZONDERQ_COSMOS_SOURCE_ROOT nie wskazuje poprawnego checkoutu Cosmos: $ZONDERQ_COSMOS_SOURCE_ROOT"
        printf '%s\n' "$(cd "$ZONDERQ_COSMOS_SOURCE_ROOT" && pwd)"
        return 0
    fi

    local parent
    local sibling
    parent="$(cd "$ROOT_DIR/.." && pwd)"
    sibling="$parent/Cosmos"

    if is_cosmos_checkout "$sibling"; then
        printf '%s\n' "$(cd "$sibling" && pwd)"
        return 0
    fi

    if is_cosmos_checkout "$parent"; then
        printf '%s\n' "$parent"
        return 0
    fi

    bootstrap_cosmos_checkout "$sibling" >&2
    printf '%s\n' "$(cd "$sibling" && pwd)"
}

cleanup_generated_tracked_changes

cosmos_root="$(resolve_cosmos_root)" || exit $?
[[ -n "$cosmos_root" ]] || fail "Nie udalo sie ustalic katalogu Cosmos."
export ZONDERQ_COSMOS_SOURCE_ROOT="$cosmos_root"
echo "[SMT] Cosmos source: $ZONDERQ_COSMOS_SOURCE_ROOT"

exec bash "$ROOT_DIR/run-core.sh" "$@"
