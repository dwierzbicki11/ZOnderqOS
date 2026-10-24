#!/usr/bin/env bash
# Offline regression: Stage2 must borrow the pinned runtime objects from
# Stage1's existing clone without modifying either upstream checkout.
set -euo pipefail
tmp="$(mktemp -d)"
trap 'rm -rf -- "$tmp"' EXIT
export GIT_ALLOW_PROTOCOL=file
git init -q "$tmp/dotnet-upstream"
git -C "$tmp/dotnet-upstream" config user.name "CI"
git -C "$tmp/dotnet-upstream" config user.email "ci@example.invalid"
mkdir -p "$tmp/dotnet-upstream/src"
printf 'pinned upstream runtime\n' > "$tmp/dotnet-upstream/src/MethodTable.cs"
git -C "$tmp/dotnet-upstream" add .
git -C "$tmp/dotnet-upstream" commit -qm 'runtime pin fixture'
git init -q "$tmp/cosmos-upstream"
git -C "$tmp/cosmos-upstream" config user.name "CI"
git -C "$tmp/cosmos-upstream" config user.email "ci@example.invalid"
git -C "$tmp/cosmos-upstream" submodule add -q "$tmp/dotnet-upstream" dotnet/runtime
git -C "$tmp/cosmos-upstream" commit -qam 'source with pinned runtime'
git -C "$tmp/cosmos-upstream" tag v3.0.95
mkdir -p "$tmp/cache/native-stage1" "$tmp/cache/native-stage2"
git clone -q --recurse-submodules "$tmp/cosmos-upstream" "$tmp/cache/native-stage1/Cosmos"
git clone -q --branch v3.0.95 "$tmp/cosmos-upstream" "$tmp/cache/native-stage2/Cosmos"

stage2="$tmp/cache/native-stage2/Cosmos"
donor="$tmp/cache/native-stage1/Cosmos/dotnet/runtime"
wanted="$(git -C "$stage2" rev-parse 'HEAD:dotnet/runtime')"
git -C "$donor" cat-file -e "$wanted^{commit}"
git -C "$stage2" submodule update --init --checkout \
  --reference "$donor" -- dotnet/runtime
actual="$(git -C "$stage2/dotnet/runtime" rev-parse HEAD)"
[[ "$actual" == "$wanted" ]] || { echo "Wrong runtime revision" >&2; exit 1; }

# The alternates file proves Git borrowed locally cached objects.
alternate="$(git -C "$stage2/dotnet/runtime" rev-parse --git-path objects/info/alternates)"
[[ -s "$alternate" ]] || { echo "No shared Git object reference" >&2; exit 1; }
git -C "$stage2" submodule update --init --recursive --checkout
[[ -f "$stage2/dotnet/runtime/src/MethodTable.cs" ]] ||
  { echo "Borrowed runtime source missing" >&2; exit 1; }
[[ "$(git -C "$stage2/dotnet/runtime" rev-parse HEAD)" == "$wanted" ]] ||
  { echo "Submodule was not pinned after recursive init" >&2; exit 1; }
echo "[3095-RUNTIME-CACHE] PASS pinned Git object reuse offline; separate worktrees"
