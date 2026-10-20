#!/usr/bin/env bash
# ZonderqOS Gen 3 entrypoint: Cosmos 3.0.94 exclusively.
#
# Do not call the obsolete SMP patch launcher from main: old packages
# are incompatible with the active Cosmos 3.0.94 project and USB drivers.
set -euo pipefail
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

case "${1:---help}" in
    --check|--status)
        bash "$ROOT_DIR/tools/verify-cosmos-compat.sh"
        bash "$ROOT_DIR/run-3094.sh" --check
        ;;
    *)
        # Read-only QEMU boot, or a CI artifact download for the exact current
        # SHA. For persistent GUI images use --download-disk and provision a
        # dedicated FAT32 ZONDERQDATA partition separately.
        exec bash "$ROOT_DIR/run-3094.sh" "$@"
        ;;
esac
