#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

fail() {
    echo "[BLAD] $*" >&2
    exit 1
}

is_cosmos_checkout() {
    local path="$1"
    [[ -d "$path" ]] || return 1
    git -C "$path" rev-parse --is-inside-work-tree >/dev/null 2>&1 || return 1
    [[ -f "$path/src/Cosmos.Kernel.Core/Runtime/Stdllib.cs" ]] || return 1
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

    echo "[BLAD] Nie znaleziono checkoutu Cosmos wymaganego dla x64 SMT." >&2
    echo "Sprawdzono:" >&2
    echo "  $sibling" >&2
    echo "  $parent" >&2
    echo "Mozesz tez ustawic ZONDERQ_COSMOS_SOURCE_ROOT=/sciezka/do/Cosmos" >&2
    exit 1
}

export ZONDERQ_COSMOS_SOURCE_ROOT="$(resolve_cosmos_root)"
echo "[SMT] Cosmos source: $ZONDERQ_COSMOS_SOURCE_ROOT"

exec "$ROOT_DIR/run-core.sh" "$@"
