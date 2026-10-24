#!/usr/bin/env bash
# Cosmos 3.0.95 migration guard. This does not certify SMP or boot.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fail() { echo "[COSMOS-3095][FAIL] $*" >&2; exit 1; }
require() {
  [[ -f "$ROOT/$1" ]] || fail "Missing file: $1"
  grep -Fq -- "$2" "$ROOT/$1" || fail "Expected in $1: $2"
}
require ZonderqOS.csproj 'Sdk="Cosmos.Sdk/3.0.95"'
require ZonderqOS.csproj 'Include="Cosmos.Kernel" Version="3.0.95"'
require ZonderqOS.csproj 'Include="Cosmos.Kernel.System" Version="3.0.95"'
require ZonderqOS.csproj '<CosmosEnableNetwork>false</CosmosEnableNetwork>'
require tools/build-cosmos-3095-local.sh 'VERSION="3.0.95"'
require tools/build-cosmos-3095-local.sh 'git clone --depth 1 --branch "v$VERSION"'
require tools/build-cosmos-3095-local.sh 'Wrong Cosmos package origin'
require tests/KeyboardApiSmoke/KeyboardApiSmoke.csproj '>3.0.95</CosmosApiTestVersion>'
require tests/KeyboardApiSmoke/KeyboardApiSmoke.csproj "'\$(CosmosApiTestVersion)' == '3.0.95'"
if grep -Eq 'Cosmos[.]Sdk/3[.]0[.](85|94)|Version="3[.]0[.](85|94)"' "$ROOT/ZonderqOS.csproj"; then
  fail "Older SDK or package version leaked into project"
fi
echo '[COSMOS-3095] PASS: project, smoke test and isolated builder version pins'
echo '[COSMOS-3095] NOT A BOOT, SMP, USB OR ETHERNET RUNTIME PROOF'
