#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

STAGE="${1:-all}"
python3 -u "$ROOT_DIR/tools/alpha1_e2e_harness.py" --stage "$STAGE"
