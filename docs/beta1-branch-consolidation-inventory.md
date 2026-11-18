# ZonderqOS — comprehensive branch and PR integration inventory

Reviewed 2026-11-18 against **main `5c17f4b170a27c0662d55b6eccde0825abbbf53b`** (post English UI PR #220). This replaces an older 28-branch snapshot. Repository contained **36 branches including main and 12 open draft PRs**. All counts below are from GitHub compare `main...branch` and represent divergent commits, not a guarantee of missing functionality or merge readiness.

## Release and safety policy

Alpha 1 test tag `v1.0.2-alpha.1` exists. Issue #211 was closed and EFI BootGuard/release-CI hardening was moved to #214 for Beta 1. The current production SDK is **Cosmos 3.0.97**, not 3.0.95. Preserve working ZONDERQDATA data and the protected USB/EFI boot chain. **Do not directly merge all historical branches, even if GitHub reports an automatic merge as syntactically possible.** Revalidate critical modules with C#, Python, QEMU and real-hardware tests as appropriate. No GC.Collect in production; SMP still paused.

## All 35 non-main branches

| Branch | Ahead | Behind | Classification | Reason / next action |
|---|---:|---:|---|---|
| `archive/smp-paused-2026-10-26` | 62 | 251 | ARCHIVED | Historic SMP work; no production merge |
| `audit/alpha1-complete-english-inventory` | 7 | 1 | SUPERSEDED | Inventory code already on main via #220; report remains on branch |
| `audit/alpha1-lexical-final-20261118` | 91 | 1 | SUPERSEDED | Second-pass diagnostics; no product change required |
| `audit/alpha1-lexical-second-pass` | 50 | 1 | SUPERSEDED | Temporary review workflow, not release code |
| `diag/failed-unlock-failpath-ram` | 7 | 129 | REWORK | PR #204; instrumentation for old base; QEMU-only review |
| `docs/issue-212-branch-inventory-20261118` | 1 | 85 | MERGE_DOCS | PR #213; update this inventory first |
| `experiment/oriongc-qemu-collect-probe` | 19 | 135 | BLOCKED | PR #203; explicit GC experiment only, never production |
| `feat/audio-gen3` | 594 | 250 | REWORK | PR #194; outdated integration with current device/SDK |
| `feat/cosmos-3095-managed-smp-stage23` | 62 | 251 | BLOCKED | Experimental managed SMP; not supported production path |
| `feat/cosmos-3095-migration` | 7 | 251 | LEGACY | Cosmos 3.0.95 migration; main on 3.0.97 |
| `feat/cosmos-3095-upstream-bsp-only` | 13 | 251 | LEGACY | Old platform branch with massive outdated diff |
| `feat/english-localization` | 0 | 83 | ALREADY_IN_MAIN | Ancestor of main |
| `feat/games-framework-gen3` | 15 | 250 | REWORK | Standalone framework promising, but uses 3.0.95 launcher and unfinished Snake/Tetris |
| `feat/gui-responsive-gen3` | 842 | 250 | REWORK | Old GUI branch; do not overwrite new layout fixes |
| `feat/memory-optimizer-10s` | 13 | 135 | REWORK | PR #201; confirm no manual GC, no idle allocations |
| `feat/memory-optimizer-main-port` | 1 | 139 | REWORK | Overlaps PR #201; consolidate before merge |
| `feat/p2-ram-oriongc-integrated` | 0 | 128 | ALREADY_IN_MAIN | Ancestor of main |
| `feat/storage-gen3` | 38 | 186 | REWORK | PR #196; FAT32 mounted data must be preserved |
| `feat/3094-qemu-e1000e-link` | 719 | 250 | REWORK | PR #189; network driver tied to older toolchain |
| `feature/real-usb-updater` | 0 | 147 | ALREADY_IN_MAIN | Ancestor of main |
| `fix/alpha1-complete-audit` | 0 | 139 | ALREADY_IN_MAIN | Ancestor of main |
| `fix/alpha1-e2e-regression` | 0 | 125 | ALREADY_IN_MAIN | Ancestor of main |
| `fix/alpha1-english-all-messages` | 73 | 2 | REVIEW_DELTA | Parallel English pass; check unique content vs merged #220 |
| `fix/alpha1-final-iso-bootguard` | 0 | 86 | ALREADY_IN_MAIN | Ancestor of main |
| `fix/alpha1-remaining-runtime-english` | 90 | 1 | ALREADY_IN_MAIN | Squash merged in #220; tree identical to main at audit start |
| `fix/alpha-hardware-idle-memory` | 0 | 135 | ALREADY_IN_MAIN | Ancestor of main |
| `fix/alpha-integrity-account-sync` | 9 | 139 | REWORK | PR #200; security/ACL changes from old base |
| `fix/alpha-readiness-audit` | 7 | 139 | REWORK | Choose only not-yet-integrated fixes |
| `fix/gen3-general-stability` | 46 | 157 | REWORK | PR #198; boot, account transaction safety on old base |
| `fix/idle-memory-background-gpt6` | 3 | 147 | REVIEW_DELTA | MemoryGuardian/UpdateManager old version; avoid replacing current updater |
| `fix/issue-211-alpha1-release` | 0 | 89 | ALREADY_IN_MAIN | Ancestor of main; release tag exists |
| `fix/p0-update-trust-gate` | 122 | 157 | BLOCKED | PR #199; independently verify current EFI BootGuard/recovery before merging |
| `fix/sysbench-baremetal-safe-report-3097` | 11 | 113 | REVIEW_DELTA | Current main already contains newer sysbench; compare unique tests |
| `fix/taskmanager-idle-memory` | 3 | 147 | REVIEW_DELTA | PR #197; overlaps current memory fixes |
| `perf/system-gen3` | 449 | 250 | REWORK | PR #195; older extensive GUI/perf source |

## All 12 open PRs at audit start

| PR | Branch | Required gate |
|---|---|---|
| [#213](https://github.com/dwierzbicki11/ZOnderqOS/pull/213) | `docs/issue-212-branch-inventory-20261118` | Documentation only; safe once updated |
| [#204](https://github.com/dwierzbicki11/ZOnderqOS/pull/204) | `diag/failed-unlock-failpath-ram` | Needs rebasing and hardware-safe probe |
| [#203](https://github.com/dwierzbicki11/ZOnderqOS/pull/203) | `experiment/oriongc-qemu-collect-probe` | Explicit GC; QEMU experiment only |
| [#201](https://github.com/dwierzbicki11/ZOnderqOS/pull/201) | `feat/memory-optimizer-10s` | Memory allocations and contracts unverified |
| [#200](https://github.com/dwierzbicki11/ZOnderqOS/pull/200) | `fix/alpha-integrity-account-sync` | Account/ACL transaction security requires retest |
| [#199](https://github.com/dwierzbicki11/ZOnderqOS/pull/199) | `fix/p0-update-trust-gate` | EFI BootGuard/slot activation safety gate |
| [#198](https://github.com/dwierzbicki11/ZOnderqOS/pull/198) | `fix/gen3-general-stability` | Old boot and account code |
| [#197](https://github.com/dwierzbicki11/ZOnderqOS/pull/197) | `fix/taskmanager-idle-memory` | Overlapping updater and memory code |
| [#196](https://github.com/dwierzbicki11/ZOnderqOS/pull/196) | `feat/storage-gen3` | Disk data integrity and mount paths |
| [#195](https://github.com/dwierzbicki11/ZOnderqOS/pull/195) | `perf/system-gen3` | Stale GUI/perf integration |
| [#194](https://github.com/dwierzbicki11/ZOnderqOS/pull/194) | `feat/audio-gen3` | Old device/runtime dependencies |
| [#189](https://github.com/dwierzbicki11/ZOnderqOS/pull/189) | `feat/3094-qemu-e1000e-link` | Old Cosmos network driver branch |

## Integration result and execution order

The eight **ALREADY_IN_MAIN** ancestor branches have no outstanding commits. The historical `fix/alpha1-remaining-runtime-english` branch contains the exact same Git tree as main due to **squash merge #220**, although its Git history is technically diverged; never replay it. Duplicate audit workflows and obsolete 3.0.95 branches are archival and should not overwrite main.

1. Update and merge documentation-only #213.
2. For each small REWORK/REVIEW_DELTA candidate, compare the **actual file changes** with current main, not simply `ahead` counts. Port only novel improvements to fresh branch. Validate isolated contracts and Cosmos 3.0.97 x64; open a separate, testable PR for each module.
3. Port games, memory optimization, GUI/perf, network, audio, storage, account security, and update trust incrementally. Keep experimental SMP and manual GC off the release branch.
4. After every accepted change, rerun relevant tests. No fabricated PASS when runner is blocked or unavailable.
5. Once preserved in main, delete stale branches **only after** verification that no unique code/history needs retaining. This connected toolset does not provide a remote branch deletion operation, so no deletion is claimed.

**Definition of Done for the user's request** is all safe, relevant changes reviewed and integrated, unsound branches preserved/archived, reproducible tests on the current SDK and a clear list of unresolved blockers. This document records the first full snapshot; it does **not** represent completion of the actual cross-branch port and regression effort.
