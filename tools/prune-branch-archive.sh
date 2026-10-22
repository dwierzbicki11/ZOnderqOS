#!/usr/bin/env bash
# One-time cleanup: archive ALL non-main branch tips as immutable lightweight
# tags, verify them on origin, then remove only their exact snapshotted refs.
# Does not merge/rebase/downgrade Cosmos or delete any unarchived commit.
set -euo pipefail

repo="dwierzbicki11/ZOnderqOS"
prefix="archive/branch-prune-20261022"
mode="${1:-}"
[[ "$mode" == "--execute" ]] || { echo "Usage: bash tools/prune-branch-archive.sh --execute" >&2; exit 2; }
[[ "${GITHUB_REPOSITORY:-}" == "$repo" ]] || { echo "[FATAL] Wrong repository" >&2; exit 1; }
[[ -n "${GH_TOKEN:-}" ]] || { echo "[FATAL] GH_TOKEN missing" >&2; exit 1; }
command -v gh >/dev/null
command -v git >/dev/null
root=$(git rev-parse --show-toplevel)
cd "$root"

# Main must be on 3.0.94 and the checkout must be at the current main SHA.
git fetch --prune origin '+refs/heads/*:refs/remotes/origin/*'
local_main=$(git rev-parse HEAD)
remote_main=$(git rev-parse refs/remotes/origin/main)
[[ "$local_main" == "$remote_main" ]] || {
  echo "[FATAL] main moved since checkout; no deletion attempted" >&2; exit 1;
}
[[ -z "$(git status --porcelain)" ]] || {
  echo "[FATAL] dirty worktree; no deletion attempted" >&2; exit 1;
}
git show refs/remotes/origin/main:ZonderqOS.csproj |
  grep -Fq 'Sdk="Cosmos.Sdk/3.0.94"' || {
    echo "[FATAL] SDK pin changed; no deletion attempted" >&2; exit 1;
  }

declare -a branches=()
declare -A sha_for=()
while read -r branch sha; do
  [[ -n "$branch" ]] || continue
  [[ "$branch" == "main" || "$branch" == "HEAD" ]] && continue
  # Branch names are derived only from live refs, not shell input.
  [[ "$branch" != -* ]] || { echo "[FATAL] invalid name" >&2; exit 1; }
  git check-ref-format "refs/tags/$prefix/$branch"
  branches+=("$branch")
  sha_for["$branch"]="$sha"
done < <(git for-each-ref --format='%(refname:lstrip=3) %(objectname)' refs/remotes/origin | LC_ALL=C sort)

report="${RUNNER_TEMP:-/tmp}/zonderq-branch-prune-report.tsv"
printf 'branch\toriginal_sha\tarchive_tag\tresult\n' > "$report"
echo "[AUDIT] Snapshot contains ${#branches[@]} non-main branches"
[[ "${#branches[@]}" -ge 1 ]] || exit 0
[[ "${#branches[@]}" -le 300 ]] || {
  echo "[FATAL] Unexpected branch count; manual audit required" >&2; exit 1;
}

# Phase 1: create local immutable archive pointers. Never retag another SHA.
declare -a pushed=()
for branch in "${branches[@]}"; do
  tag="$prefix/$branch"
  expected="${sha_for[$branch]}"
  if git show-ref --verify --quiet "refs/tags/$tag"; then
    actual=$(git rev-parse "refs/tags/$tag")
    [[ "$actual" == "$expected" ]] || {
      echo "[FATAL] archive tag collision: $tag" >&2; exit 1;
    }
  else
    git tag "$tag" "$expected"
  fi
  pushed+=("refs/tags/$tag:refs/tags/$tag")
done

# Phase 2: upload every archive ref first, in bounded pushes. One failure
# aborts before touching ANY remote branch. Re-running is safe.
declare -a batch=()
for spec in "${pushed[@]}"; do
  batch+=("$spec")
  if (( ${#batch[@]} >= 20 )); then
    git push origin "${batch[@]}"
    batch=()
  fi
done
if (( ${#batch[@]} > 0 )); then
  git push origin "${batch[@]}"
fi

# Verify every exact branch-tip SHA is retrievable as a tag on the SERVER.
declare -A remote_archive=()
while IFS=$'\t' read -r oid ref; do
  if [[ "$ref" == "refs/tags/$prefix/"* ]]; then
    remote_archive["${ref#refs/tags/}"]="$oid"
  fi
done < <(git ls-remote --refs origin)

for branch in "${branches[@]}"; do
  tag="$prefix/$branch"
  expected="${sha_for[$branch]}"
  [[ "${remote_archive[$tag]:-}" == "$expected" ]] || {
    echo "[FATAL] Remote archive incomplete/mismatched: $tag; no branch deleted" >&2
    exit 1
  }
done
echo "[ARCHIVE] Verified ${#branches[@]} unique remote snapshot tags; safe to begin cleanup"

# Phase 3: close open same-repository PRs pointing at archived branches.
# These are NOT merged; users can recreate a branch from the archive tag.
open_prs="${RUNNER_TEMP:-/tmp}/zonderq-open-pr-heads.tsv"
gh pr list --repo "$repo" --state open --limit 500 \
  --json number,headRefName,isCrossRepository \
  --jq '.[] | select(.isCrossRepository == false) | [.number,.headRefName] | @tsv' > "$open_prs"

while IFS=$'\t' read -r pr branch; do
  [[ -n "$pr" && -n "$branch" ]] || continue
  [[ -n "${sha_for[$branch]:-}" ]] || continue
  tag="$prefix/$branch"
  gh pr comment "$pr" --repo "$repo" --body \
    "Zamknięte podczas porządkowania branchy. **Nie scalono tego PR-a.** Pełna historia starego brancha została zachowana pod tagiem \`$tag\` (SHA \`${sha_for[$branch]}\`). Można go odtworzyć poleceniem \`git switch -c $branch $tag\`." ||
    echo "[WARN] Cannot comment PR #$pr"
  gh pr close "$pr" --repo "$repo" ||
    echo "[WARN] Cannot close PR #$pr (branch is archived)"
done < "$open_prs"

# Phase 4: lease-protected deletes. If an agent/collaborator advanced its
# branch after our snapshot, skip instead of destroying unarchived work.
removed=0
skipped=0
for branch in "${branches[@]}"; do
  expected="${sha_for[$branch]}"
  tag="$prefix/$branch"
  if git push --force-with-lease="refs/heads/$branch:$expected" \
      origin ":refs/heads/$branch"; then
    result="deleted"
    removed=$((removed + 1))
  else
    result="kept_moved_or_protected"
    skipped=$((skipped + 1))
  fi
  printf '%s\t%s\t%s\t%s\n' "$branch" "$expected" "$tag" "$result" >> "$report"
done

echo "[RESULT] Removed=$removed; kept=$skipped; all snapshotted heads archived"
echo "[RESULT] Report=$report"
if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
  {
    echo "## ZonderqOS branch cleanup"
    echo "- Archived ${#branches[@]} branch heads as verified immutable tags."
    echo "- Deleted **$removed** non-main branches."
    echo "- Kept **$skipped** moved or protected branches."
    echo "- Left \`main\` and Cosmos SDK **3.0.94** untouched."
    echo "- Open PRs for archived branches were closed without merging."
  } >> "$GITHUB_STEP_SUMMARY"
fi
