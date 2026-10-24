#!/usr/bin/env bash
# Recover ONLY a broken dotnet/runtime submodule of the private Cosmos 3.0.95
# build cache. Backup, never delete, existing module data before recloning.
set -euo pipefail

fail() { printf '[3095-RUNTIME-RECOVERY][FAIL] %s\n' "$*" >&2; exit 1; }
[[ $# -eq 2 ]] || fail "usage: repair-cosmos-3095-runtime-submodule.sh SOURCE WORK_ROOT"
SOURCE="$(realpath -e -- "$1")"
WORK_ROOT="$(realpath -e -- "$2")"
[[ "$SOURCE" == "$WORK_ROOT/Cosmos" ]] ||
  fail "refusing checkout outside the expected private Cosmos workspace"
[[ -d "$SOURCE/.git" && ! -L "$SOURCE/.git" ]] ||
  fail "expected a normal isolated Git clone, not a linked worktree or symlink"
[[ "$(git -C "$SOURCE" rev-parse HEAD)" == "$(git -C "$SOURCE" rev-parse 'refs/tags/v3.0.95^{commit}')" ]] ||
  fail "not the exact Cosmos v3.0.95 tag"

expected="$(git -C "$SOURCE" rev-parse 'HEAD:dotnet/runtime')" ||
  fail "dotnet/runtime gitlink not found at the tagged commit"
[[ "$expected" =~ ^[0-9a-f]{40}$ ]] || fail "invalid pinned runtime revision"
sub="$SOURCE/dotnet/runtime"
module="$SOURCE/.git/modules/dotnet/runtime"
[[ ! -L "$sub" && ! -L "$module" ]] ||
  fail "refusing symlinked submodule path"
[[ ! -e "$module" || -d "$module" ]] ||
  fail "submodule Git metadata is not a directory"
[[ ! -e "$sub" || -d "$sub" ]] ||
  fail "runtime submodule worktree is not a directory"

current=""
if [[ -d "$sub" ]]; then
  # Without its own .git, git -C "$sub" silently climbs to the Cosmos
  # superproject and reports the WRONG HEAD. Guard the actual worktree root.
  runtime_top="$(git -C "$sub" rev-parse --show-toplevel 2>/dev/null || true)"
  if [[ "$runtime_top" == "$sub" ]]; then
    current="$(git -C "$sub" rev-parse --verify 'HEAD^{commit}' 2>/dev/null || true)"
  fi
fi
if [[ "$current" == "$expected" ]]; then
  fail "runtime HEAD is already pinned ($expected); preserve worktree and inspect missing files"
fi
if [[ -n "$current" ]]; then
  fail "runtime has a different valid HEAD ($current); refusing automatic replacement"
fi

umask 077
backup="$(mktemp -d "$WORK_ROOT/runtime-recovery-XXXXXXXX")" ||
  fail "cannot allocate private backup directory"
printf '[3095-RUNTIME-RECOVERY] Missing runtime HEAD; expected pinned commit: %s\n' "$expected"
printf '[3095-RUNTIME-RECOVERY] Preserving old checkout and Git data in: %s\n' "$backup"
if [[ -d "$sub" ]]; then
  mv -- "$sub" "$backup/runtime-worktree" || fail "cannot preserve old runtime worktree"
fi
if [[ -d "$module" ]]; then
  mv -- "$module" "$backup/runtime-gitdir" || fail "cannot preserve old runtime git metadata"
fi

git -C "$SOURCE" submodule update --init --checkout --recursive -- dotnet/runtime ||
  fail "fresh pinned runtime checkout failed; backup retained in $backup"
restored="$(git -C "$sub" rev-parse --verify 'HEAD^{commit}')" ||
  fail "new runtime clone has no valid HEAD"
[[ "$restored" == "$expected" ]] ||
  fail "new runtime clone did not checkout pinned revision ($restored != $expected)"
printf '[3095-RUNTIME-RECOVERY] PASS: restored exact runtime %s; backup retained at %s\n' "$restored" "$backup"
