# ZonderqOS — branch inventory

Generated UTC: 2026-10-22T16:34:39.777297+00:00
main commit: bae70642ae67cf44681d1af0e99e9d028ea20c1c
Canonical SDK/runtime: Cosmos 3.0.94 (no downgrade).

This is a read-only inventory. It does not merge, delete, or certify SMP.

## Summary

Non-main branches: 1

- NEEDS_PORT_AND_TEST: 1

## Classification

- HISTORY_IN_MAIN: branch tip already reachable from main
- SAME_TREE: all tracked content matches main, but history differs
- MERGE_NO_CHANGE: clean merge changes no main files
- NEEDS_PORT_AND_TEST: would change main; requires gated tests
- CONFLICT: cannot merge automatically; selective port required

## Remaining branches

| Branch | State | Risk | Behind / Ahead | Files | PR |
|---|---|---|---:|---:|---|
| feat/3094-pci-nic-inventory | NEEDS_PORT_AND_TEST | KERNEL_OR_BUILD | 1 / 7 | 7 | [#188](https://github.com/dwierzbicki11/ZOnderqOS/pull/188) |

## Largest pending deltas

- feat/3094-pci-nic-inventory: .github/workflows/3094-nic-inventory.yml, Kernel.cs, docs/3094-network-bringup.md, source/Core/NetworkPciDiagnostics.cs, source/Shell/Command.cs, source/Shell/Commands/CmdNicDiag.cs, tests/test_3094_nic_pci_inventory.py

## Safety gates

Do not automatically merge changes touching the kernel, managed SMP, OrionGC, disks, security, network or hardware drivers. Require matching Cosmos 3.0.94 packages, builds, QEMU tests and relevant physical validation. Do not mistake contract tests for hardware proof.
