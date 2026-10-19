#!/usr/bin/env bash
# Isolated Cosmos v3.0.94 stage-1 SMP port verifier.
# Never updates the production 3.0.85 SDK, patches, launcher or working tree.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PATCH="$ROOT/patches/cosmos-3094/0001-limine-mp-discovery.patch"
COSMOS_SOURCE="${ZONDERQ_COSMOS_SOURCE_ROOT:-}"
if [[ ! -d "$COSMOS_SOURCE/.git" ]]; then
    echo "[3094] Set ZONDERQ_COSMOS_SOURCE_ROOT to a clean Cosmos v3.0.94 checkout." >&2
    exit 2
fi
tag="$(git -C "$COSMOS_SOURCE" rev-list -n 1 v3.0.94)"
head="$(git -C "$COSMOS_SOURCE" rev-parse HEAD)"
if [[ "$head" != "$tag" ]]; then
    echo "[3094] Refusing to run against a non-v3.0.94 HEAD." >&2
    exit 2
fi
if [[ -n "$(git -C "$COSMOS_SOURCE" status --porcelain)" ]]; then
    echo "[3094] Refusing to change dirty Cosmos sources." >&2
    exit 2
fi
git -C "$COSMOS_SOURCE" apply --check "$PATCH"
echo "[3094] Stage 1 patch applies cleanly to Cosmos v3.0.94."
if [[ "${1:-}" == "--apply" ]]; then
    git -C "$COSMOS_SOURCE" apply "$PATCH"
    echo "[3094] Stage 1 discovery patch applied to isolated checkout."
elif [[ $# -ne 0 ]]; then
    echo "Usage: $0 [--apply]" >&2
    exit 2
fi
