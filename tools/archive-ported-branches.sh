#!/usr/bin/env bash
# Archive two old PR branches only after their unique functionality has
# been selectively ported onto main. All other branches stay untouched.
set -euo pipefail
[[ "${GITHUB_REPOSITORY:-}" == "dwierzbicki11/ZOnderqOS" ]] || exit 1
[[ "${1:-}" == "--execute" ]] || { echo "Usage: $0 --execute" >&2; exit 2; }
[[ -n "${GH_TOKEN:-}" ]] || exit 1
command -v gh >/dev/null

git fetch --prune origin '+refs/heads/*:refs/remotes/origin/*'

git show refs/remotes/origin/main:ZonderqOS.csproj | grep -F 'Cosmos.Sdk/3.0.94' >/dev/null
git show refs/remotes/origin/main:tools/verify-cosmos-3094-managed-smp-contract.py |
  grep -F 'if not gc_index < index < finish_index:' >/dev/null
git show refs/remotes/origin/main:docs/3094-normal-gui-display.md |
  grep -F '## Diagnostic command after migration of PR #174' >/dev/null

verify_scope() {
  local name="$1" base
  shift
  base=$(git merge-base refs/remotes/origin/main "refs/remotes/origin/$name")
  diff -u <(printf '%s\n' "$@" | LC_ALL=C sort) \
          <(git diff --name-only "$base" "refs/remotes/origin/$name" | LC_ALL=C sort)
}

archive_pr() {
  local branch="$1" expected="$2" pr="$3" tag="$4"
  local head state old_archive
  state=$(gh api "repos/$GITHUB_REPOSITORY/pulls/$pr" --jq '.state')
  [[ "$state" == "closed" ]] || { echo "[ARCHIVE] PR $pr is open" >&2; exit 1; }
  head=$(git ls-remote origin "refs/heads/$branch" | cut -f1)
  if [[ -z "$head" ]]; then
    echo "[ARCHIVE] Already removed: $branch"
    return
  fi
  [[ "$head" == "$expected" ]] || { echo "[ARCHIVE] Unexpected new commits on $branch" >&2; exit 1; }
  old_archive=$(git ls-remote origin "refs/tags/$tag" | cut -f1)
  if [[ -z "$old_archive" ]]; then
    git tag "$tag" "$expected"
    git push origin "refs/tags/$tag"
    old_archive=$(git ls-remote origin "refs/tags/$tag" | cut -f1)
  fi
  [[ "$old_archive" == "$expected" ]] || { echo "[ARCHIVE] Archive tag mismatch" >&2; exit 1; }
  git push --force-with-lease="refs/heads/$branch:$expected" origin ":refs/heads/$branch"
  echo "[ARCHIVE] Preserved $branch as $tag, deleted branch"
}

verify_existing_archive() {
  local tag="$1" expected="$2" actual
  actual=$(git ls-remote origin "refs/tags/$tag" | cut -f1)
  [[ "$actual" == "$expected" ]] || { echo "[ARCHIVE] Historical head missing: $tag" >&2; exit 1; }
  echo "[ARCHIVE] Verified existing archival tag: $tag"
}

if git show-ref --verify --quiet refs/remotes/origin/fix/3094-display-hardware-diagnostics; then
# PR #174: exact diagnostics source plus registration/test and explanatory docs.
verify_scope fix/3094-display-hardware-diagnostics \
  docs/3094-normal-gui-display.md \
  source/Shell/Command.cs \
  source/Shell/Commands/CmdDisplayInfo.cs \
  tests/test_3094_display_gui.py
[[ "$(git rev-parse refs/remotes/origin/main:source/Shell/Commands/CmdDisplayInfo.cs)" == \
   "$(git rev-parse refs/remotes/origin/fix/3094-display-hardware-diagnostics:source/Shell/Commands/CmdDisplayInfo.cs)" ]] || exit 1
git show refs/remotes/origin/main:source/Shell/Command.cs |
  grep -F '_commands.Add(new CmdDisplayInfo())' >/dev/null
git show refs/remotes/origin/main:tests/test_3094_display_gui.py |
  grep -F 'test_displayinfo_is_diagnostic' >/dev/null

archive_pr fix/3094-display-hardware-diagnostics \
  94578e6559e8971e0dda718d3f779384d49294b3 174 \
  archive/ported-pr174-displayinfo-3094

else
  verify_existing_archive archive/ported-pr174-displayinfo-3094 94578e6559e8971e0dda718d3f779384d49294b3
fi

if git show-ref --verify --quiet refs/remotes/origin/feat/3094-smp-full-managed-port-gates; then
# PR #168: verifier, workflow and documentation are now on main.
verify_scope feat/3094-smp-full-managed-port-gates \
  .github/workflows/cosmos-3094-managed-contract.yml \
  docs/3094-managed-smt-execution-plan.md \
  tools/verify-cosmos-3094-managed-smp-contract.py
git show refs/remotes/origin/main:.github/workflows/cosmos-3094-managed-contract.yml |
  grep -F 'python3 tools/verify-cosmos-3094-managed-smp-contract.py --self-test' >/dev/null
git show refs/remotes/origin/main:docs/3094-managed-smt-execution-plan.md |
  grep -F 'Thread.Start' >/dev/null

archive_pr feat/3094-smp-full-managed-port-gates \
  add50c0b9197d40ab1346ed62f5bfbe343f15b74 168 \
  archive/ported-pr168-managed-smp-contract-3094

else
  verify_existing_archive archive/ported-pr168-managed-smp-contract-3094 add50c0b9197d40ab1346ed62f5bfbe343f15b74
fi
