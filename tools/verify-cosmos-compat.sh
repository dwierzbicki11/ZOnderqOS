#!/usr/bin/env bash
# Verify the active ZonderqOS project, launcher and dedicated CI path all
# agree on Cosmos 3.0.94. This is NOT a managed Stage-23 SMP proof.
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="3.0.94"

fail() {
    printf '[COSMOS-VERSION][FAIL] %s\n' "$*" >&2
    exit 1
}
require_literal() {
    local relative="$1" literal="$2"
    [[ -f "$ROOT_DIR/$relative" ]] || fail "Missing $relative"
    grep -Fq -- "$literal" "$ROOT_DIR/$relative" ||
        fail "$relative does not contain expected 3.0.94 anchor: $literal"
}

require_literal ZonderqOS.csproj "<Project Sdk=\"Cosmos.Sdk/$VERSION\">"
require_literal ZonderqOS.csproj "<ZonderqCosmosKeyboardApi>Input</ZonderqCosmosKeyboardApi>"
require_literal ZonderqOS.csproj "Include=\"Cosmos.Kernel\" Version=\"$VERSION\""
require_literal ZonderqOS.csproj "Include=\"Cosmos.Kernel.System\" Version=\"$VERSION\""

require_literal run.sh 'run-3094.sh'
require_literal run-3094.sh 'Sdk="Cosmos.Sdk/3.0.94"'
require_literal .github/workflows/experimental-cosmos-3094-usb.yml "ref: v$VERSION"
require_literal .github/workflows/experimental-cosmos-3094-usb.yml "VersionPrefix: '$VERSION'"
require_literal .github/workflows/experimental-cosmos-3094-usb.yml 'ZonderqRamRoot=false'
require_literal .github/workflows/experimental-cosmos-3094-usb.yml 'QEMU persistent FAT32 data volume'
require_literal .github/workflows/keyboard-api-smoke.yml "'$VERSION'"
require_literal tests/KeyboardApiSmoke/KeyboardApiSmoke.csproj "<CosmosApiTestVersion Condition=\"'\$(CosmosApiTestVersion)' == ''\">$VERSION</CosmosApiTestVersion>"

# A compatibility check must not bless the outdated Stage-22/23 v3.0.85
# patch series as if it were already ported. The separate, real production
# managed SMP/OrionGC gate remains BLOCKED until the v3.0.94 port exists.
if grep -Fq 'v3.0.85' "$ROOT_DIR/run.sh"; then
    fail "Active main launcher still references incompatible Cosmos v3.0.85"
fi

printf '[COSMOS-VERSION] PASS: active SDK, packages, launcher, input API, persistent GUI CI = %s\n' "$VERSION"
printf '[COSMOS-VERSION] NOTE: production managed SMP/OrionGC Stage 23 is NOT yet certified on %s.\n' "$VERSION"
