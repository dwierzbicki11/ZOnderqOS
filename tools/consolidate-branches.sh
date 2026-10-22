#!/usr/bin/env bash
# Consolidate only branches whose merge changes NO tracked file on main.
# Branches with novel code remain untouched for a separate, tested merge.
set -euo pipefail

case "${1:---dry-run}" in
  --dry-run) apply=false ;;
  --execute) apply=true ;;
  *) echo 'Usage: consolidate-branches.sh [--dry-run|--execute]' >&2; exit 2 ;;
esac

repo=$(git rev-parse --show-toplevel)
cd "$repo"
[[ -z "$(git status --porcelain)" ]] || { echo 'Working tree must be clean' >&2; exit 1; }
if [[ "${GITHUB_REPOSITORY:-dwierzbicki11/ZOnderqOS}" != 'dwierzbicki11/ZOnderqOS' ]]; then
  echo 'Only run for dwierzbicki11/ZOnderqOS' >&2; exit 1
fi

git fetch --prune origin '+refs/heads/*:refs/remotes/origin/*'
base_sha=$(git rev-parse refs/remotes/origin/main)
base_tree=$(git rev-parse "${base_sha}^{tree}")

work_dir=$(mktemp -d)
work_branch="maintenance/consolidation-$$"
clean() {
  git worktree remove --force "$work_dir" >/dev/null 2>&1 || true
  git branch -D "$work_branch" >/dev/null 2>&1 || true
  rmdir "$work_dir" >/dev/null 2>&1 || true
}
trap clean EXIT

git worktree add --quiet -b "$work_branch" "$work_dir" "$base_sha"
git -C "$work_dir" config user.name 'ZonderqOS Branch Consolidation'
git -C "$work_dir" config user.email '41898282+github-actions[bot]@users.noreply.github.com'

declare -a integrated=()
declare -a skipped=()
declare -a branches=()
while IFS= read -r b; do
  [[ -n "$b" && "$b" != 'main' && "$b" != 'HEAD' ]] && branches+=("$b")
done < <(git for-each-ref --format='%(refname:lstrip=3)' refs/remotes/origin | LC_ALL=C sort)

printf 'Found %d non-main branches (mode: %s)\n' "${#branches[@]}" "$([[ "$apply" == true ]] && echo execute || echo dry-run)"

for b in "${branches[@]}"; do
  ref="refs/remotes/origin/$b"
  before_tree=$(git -C "$work_dir" rev-parse 'HEAD^{tree}')
  if git -C "$work_dir" merge-base --is-ancestor "$ref" HEAD; then
    integrated+=("$b")
    printf '[ALREADY-MERGED] %s\n' "$b"
    continue
  fi

  # Compute a real three-way merge and accept it ONLY if its tree is
  # identical to current main; this cannot reintroduce stale SDK or code.
  if candidate_tree=$(git -C "$work_dir" merge-tree --write-tree HEAD "$ref" 2>/dev/null) && \
     [[ "$candidate_tree" == "$before_tree" ]]; then
    if git -C "$work_dir" merge --no-ff --no-edit -m "chore: retain merged history of $b" "$ref" >/dev/null 2>&1 && \
       [[ "$(git -C "$work_dir" rev-parse 'HEAD^{tree}')" == "$before_tree" ]]; then
      integrated+=("$b")
      printf '[NO-CHANGE-MERGE] %s\n' "$b"
      continue
    fi
    echo "Unexpected merge result for $b; refusing to proceed" >&2
    exit 1
  fi
  skipped+=("$b")
  printf '[REQUIRES-REVIEW] %s\n' "$b"
done

printf '\nContent-preserving merges: %d; requiring review: %d\n' "${#integrated[@]}" "${#skipped[@]}"
report="${BRANCH_CONSOLIDATION_REPORT:-${TMPDIR:-/tmp}/zonderq-branches-skipped-$$.txt}"
printf '%s\n' "${skipped[@]}" > "$report"
printf 'Skipped-branch report: %s\n' "$report"

# All merges must leave the main tree byte-for-byte identical. Verify
# Cosmos 3.0.94 and critical storage/input contract tests before writes.
[[ "$(git -C "$work_dir" rev-parse 'HEAD^{tree}')" == "$base_tree" ]] || {
  echo 'Integration changed main file tree: refusing to push' >&2; exit 1;
}
(
  cd "$work_dir"
  bash tools/verify-cosmos-compat.sh
  python3 tests/test_3094_storage_policy.py
  python3 tests/test_3094_mouse_gui.py
)

if [[ "$apply" != true ]]; then
  echo 'DRY RUN. No remote changes. Use --execute to merge no-change histories and prune ONLY merged branches.'
  exit 0
fi

actual_main=$(git ls-remote --exit-code origin refs/heads/main | cut -f1)
[[ "$actual_main" == "$base_sha" ]] || {
  echo 'Remote main changed during audit: refusing to push or delete' >&2; exit 1;
}

if [[ "$(git -C "$work_dir" rev-parse HEAD)" != "$base_sha" ]]; then
  git -C "$work_dir" push origin HEAD:refs/heads/main
fi

git fetch origin main:refs/remotes/origin/main
pruned=0
for b in "${integrated[@]}"; do
  ref="refs/remotes/origin/$b"
  git merge-base --is-ancestor "$ref" refs/remotes/origin/main || {
    printf '[SKIP-DELETE] %s not reachable from main\n' "$b"; continue;
  }
  expected=$(git rev-parse "$ref")
  if git push --force-with-lease="refs/heads/$b:$expected" origin ":refs/heads/$b"; then
    pruned=$((pruned + 1))
  else
    printf '[SKIP-DELETE] %s changed/protected or deletion denied\n' "$b"
  fi
done
printf 'Completed: %d branches deleted after integration; %d held for manual review.\n' "$pruned" "${#skipped[@]}"
