#!/usr/bin/env bash
# Fail fast if the Cosmos SDK diverges from the locally patched Stage 23 runtime.
# Update this guard only after the complete patch series has been ported and
# the real QEMU/ARM64 compatibility checks pass on the target release.
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BASE_VERSION="3.0.85"

fail() {
    printf '[COSMOS-VERSION][FAIL] %s\n' "$*" >&2
    exit 1
}

require_literal() {
    local file="$1" literal="$2"
    grep -Fq -- "$literal" "$ROOT_DIR/$file" ||
        fail "$file does not contain required release anchor: $literal"
}

project="$ROOT_DIR/ZonderqOS.csproj"
[[ -f "$project" ]] || fail "Missing ZonderqOS.csproj"

# The project must use the *same* Cosmos release for MSBuild SDK and runtime.
require_literal "ZonderqOS.csproj" "<Project Sdk=\"Cosmos.Sdk/$BASE_VERSION\">"
require_literal "ZonderqOS.csproj" "Include=\"Cosmos.Kernel\" Version=\"$BASE_VERSION\""
require_literal "ZonderqOS.csproj" "Include=\"Cosmos.Kernel.System\" Version=\"$BASE_VERSION\""

# These tools build a custom local NuGet feed with the Stage 23 SMP changes.
# A new public SDK cannot use the old locally built packages.
require_literal "run.sh" "COSMOS_TAG=\"v$BASE_VERSION\""
require_literal "run-core.sh" "Cosmos.Kernel.$BASE_VERSION.nupkg"
require_literal "tools/prepare-cosmos-smt.sh" "v$BASE_VERSION"
require_literal "tools/prepare-cosmos-smt.sh" "VersionPrefix=$BASE_VERSION"
require_literal ".github/workflows/smt-stage22.yml" "ref: v$BASE_VERSION"
require_literal ".github/workflows/smt-stage23.yml" "ref: v$BASE_VERSION"
require_literal ".github/workflows/smt-stage22.yml" "cosmos.*/$BASE_VERSION/"
require_literal ".github/workflows/smt-stage23.yml" "cosmos.*/$BASE_VERSION/"

printf '[COSMOS-VERSION] PASS: Cosmos SDK/packages, launcher, SMP patches and CI use %s\n' "$BASE_VERSION"
