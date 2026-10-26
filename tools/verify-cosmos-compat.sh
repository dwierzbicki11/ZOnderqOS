#!/usr/bin/env bash
# Active, fail-closed production-track contract for Cosmos v3.0.95.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
die() { printf '[COSMOS-3095-STOCK][FAIL] %s\n' "$*" >&2; exit 1; }
bash "$ROOT/tools/verify-cosmos-3095-migration.sh"
for obsolete in \
  patches/cosmos-smt patches/cosmos-3094 \
  tools/prepare-cosmos-smt.sh tools/check-cosmos-3094-stage1.sh \
  run-core.sh run-3094.sh \
  .github/workflows/smt-stage23.yml .github/workflows/smt-stage22.yml; do
  [[ ! -e "$ROOT/$obsolete" ]] || die "Retired private SMP input still present: $obsolete"
done
grep -Fq -- '-smp 1' "$ROOT/run.sh" ||
  die "QEMU must be pinned to 1 vCPU while upstream SMP is not certified"
grep -Fq -- 'exec bash "$ROOT/tools/build-cosmos-3095-local.sh" disk' "$ROOT/run.sh" ||
  die "Normal launcher must build stock v3.0.95 packages"
grep -Fq 'ZONDERQ_3095_NATIVE_STAGE4' "$ROOT/tools/build-cosmos-3095-local.sh" ||
  die "Experimental SMP opt-in guard is missing"
printf '[COSMOS-3095-STOCK] PASS: upstream v3.0.95, no patched SMP builder, QEMU -smp 1\n'
printf '[COSMOS-3095-STOCK] NOTE: BSP-only hardware boot, USB and persistence remain to be validated.\n'
