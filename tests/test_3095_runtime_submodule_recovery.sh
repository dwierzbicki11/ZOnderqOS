#!/usr/bin/env bash
# Exercise the recovery algorithm entirely in disposable local Git repos.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TMP="$(mktemp -d)"
trap 'rm -rf -- "$TMP"' EXIT
export GIT_ALLOW_PROTOCOL=file
git init -q "$TMP/runtime-source"
git -C "$TMP/runtime-source" config user.name CI
git -C "$TMP/runtime-source" config user.email ci@example.invalid
mkdir -p "$TMP/runtime-source/src"
printf 'test\n' > "$TMP/runtime-source/src/MethodTable.cs"
git -C "$TMP/runtime-source" add .
git -C "$TMP/runtime-source" commit -qm 'synthetic source'
git init -q "$TMP/cosmos-source"
git -C "$TMP/cosmos-source" config user.name CI
git -C "$TMP/cosmos-source" config user.email ci@example.invalid
git -C "$TMP/cosmos-source" submodule add -q "$TMP/runtime-source" dotnet/runtime
git -C "$TMP/cosmos-source" commit -qam 'synthetic Cosmos tag'
git -C "$TMP/cosmos-source" tag v3.0.95
mkdir -p "$TMP/private"
git clone -q --recurse-submodules "$TMP/cosmos-source" "$TMP/private/Cosmos"

# Deliberately make runtime's HEAD inaccessible without modifying source.
sub="$TMP/private/Cosmos/dotnet/runtime"
module="$TMP/private/Cosmos/.git/modules/dotnet/runtime"
mv "$module/HEAD" "$module/HEAD.broken"
bash "$ROOT/tools/repair-cosmos-3095-runtime-submodule.sh" "$TMP/private/Cosmos" "$TMP/private"

expected="$(git -C "$TMP/private/Cosmos" rev-parse 'HEAD:dotnet/runtime')"
actual="$(git -C "$sub" rev-parse 'HEAD^{commit}')"
[[ "$actual" == "$expected" ]]
compgen -G "$TMP/private/runtime-recovery-*/runtime-gitdir/HEAD.broken" >/dev/null ||
  { echo "recovery discarded broken original metadata" >&2; exit 1; }
if bash "$ROOT/tools/repair-cosmos-3095-runtime-submodule.sh" "$TMP/private/Cosmos" "$TMP/private" >"$TMP/refusal.log" 2>&1; then
  echo "recovery should refuse replacing healthy current runtime" >&2
  exit 1
fi
grep -Fq 'runtime HEAD is already pinned' "$TMP/refusal.log"
echo "[3095-RUNTIME-RECOVERY] PASS corrupted cache recovery and healthy cache refusal"
