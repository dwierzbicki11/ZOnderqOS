# ZonderqOS — branch inventory

Generated UTC: 2026-10-22T15:42:05.644422+00:00
main commit: 6030fe479d6cf86ac1b0627390d1e91c481a8a77
Canonical SDK/runtime: Cosmos 3.0.94 (no downgrade).

This is a read-only inventory. It does not merge, delete, or certify SMP.

## Summary

Non-main branches: 125

- CONFLICT: 106
- NEEDS_PORT_AND_TEST: 19

## Classification

- HISTORY_IN_MAIN: branch tip already reachable from main
- SAME_TREE: all tracked content matches main, but history differs
- MERGE_NO_CHANGE: clean merge changes no main files
- NEEDS_PORT_AND_TEST: would change main; requires gated tests
- CONFLICT: cannot merge automatically; selective port required

## Remaining branches

| Branch | State | Risk | Behind / Ahead | Files | PR |
|---|---|---|---:|---:|---|
| ai/usb-1-testy | NEEDS_PORT_AND_TEST | LEGACY_SDK | 240 / 2 | 2 |  |
| ai/usb-2-smp | NEEDS_PORT_AND_TEST | LEGACY_SDK | 240 / 2 | 3 |  |
| ai/usb-3-cosmos3094 | NEEDS_PORT_AND_TEST | LEGACY_SDK | 240 / 1 | 2 | [#152](https://github.com/dwierzbicki11/ZOnderqOS/pull/152) |
| ai/usb-4-shell | NEEDS_PORT_AND_TEST | LEGACY_SDK | 240 / 2 | 3 |  |
| ai/usb-5-gui | NEEDS_PORT_AND_TEST | LEGACY_SDK | 240 / 2 | 5 |  |
| arm64-diagnostics-fallback | NEEDS_PORT_AND_TEST | LEGACY_SDK | 753 / 4 | 2 |  |
| arm64-xhci-usb-keyboard-shell | NEEDS_PORT_AND_TEST | LEGACY_SDK | 766 / 3 | 3 |  |
| copilot/expand-system-development | NEEDS_PORT_AND_TEST | LEGACY_SDK | 261 / 1 | 4 | [#144](https://github.com/dwierzbicki11/ZOnderqOS/pull/144) |
| feat/3094-smp-full-managed-port-gates | NEEDS_PORT_AND_TEST | KERNEL_OR_BUILD | 150 / 3 | 3 | [#168](https://github.com/dwierzbicki11/ZOnderqOS/pull/168) |
| feat/3094-smp-stage12-managed-ap-proof | NEEDS_PORT_AND_TEST | KERNEL_OR_BUILD | 148 / 3 | 5 | [#170](https://github.com/dwierzbicki11/ZOnderqOS/pull/170) |
| feature/log-query | NEEDS_PORT_AND_TEST | LEGACY_SDK | 523 / 6 | 5 | [#88](https://github.com/dwierzbicki11/ZOnderqOS/pull/88) |
| feature/system-installer-foundation | NEEDS_PORT_AND_TEST | LEGACY_SDK | 301 / 4 | 4 |  |
| fix/3094-cs0016-generator-compile-gates | NEEDS_PORT_AND_TEST | APP_OR_TOOL | 137 / 7 | 5 |  |
| fix/3094-display-hardware-diagnostics | NEEDS_PORT_AND_TEST | APP_OR_TOOL | 145 / 5 | 4 | [#174](https://github.com/dwierzbicki11/ZOnderqOS/pull/174) |
| fix/3094-smt-stage23-ci-preflight | NEEDS_PORT_AND_TEST | KERNEL_OR_BUILD | 151 / 2 | 2 | [#165](https://github.com/dwierzbicki11/ZOnderqOS/pull/165) |
| gui-modern3 | NEEDS_PORT_AND_TEST | LEGACY_SDK | 1020 / 1 | 1 |  |
| kernel/ring3-syscall-foundation | NEEDS_PORT_AND_TEST | LEGACY_SDK | 628 / 9 | 4 | [#68](https://github.com/dwierzbicki11/ZOnderqOS/pull/68) |
| smp/runtime-all-cpus | NEEDS_PORT_AND_TEST | LEGACY_SDK | 643 / 3 | 3 |  |
| smt/night-2026-09-18-work-proof | NEEDS_PORT_AND_TEST | LEGACY_SDK | 671 / 3 | 1 |  |
| ai/usb-10-testy | CONFLICT | LEGACY_SDK | 240 / 3 | 128 |  |
| ai/usb-12-shell | CONFLICT | LEGACY_SDK | 240 / 3 | 128 |  |
| ai/usb-13-gui | CONFLICT | LEGACY_SDK | 240 / 3 | 128 |  |
| ai/usb-6-testy | CONFLICT | LEGACY_SDK | 240 / 3 | 127 |  |
| ai/usb-7-smp | CONFLICT | LEGACY_SDK | 240 / 3 | 129 |  |
| ai/usb-8-shell | CONFLICT | LEGACY_SDK | 240 / 3 | 129 |  |
| ai/usb-9-gui | CONFLICT | LEGACY_SDK | 240 / 3 | 131 |  |
| arm64-bcm2711-pci-xhci-probe | CONFLICT | LEGACY_SDK | 769 / 4 | 588 |  |
| arm64-cosmos-devicemapper-xhci | CONFLICT | LEGACY_SDK | 767 / 3 | 588 |  |
| arm64-direct-framebuffer-probe | CONFLICT | LEGACY_SDK | 776 / 2 | 587 |  |
| arm64-disable-qemu-uart | CONFLICT | LEGACY_SDK | 775 / 1 | 587 |  |
| arm64-full-desktop-parity | CONFLICT | LEGACY_SDK | 757 / 7 | 582 |  |
| arm64-irq-gic-reenable-test | CONFLICT | LEGACY_SDK | 768 / 2 | 588 |  |
| arm64-native-xhci-stage1 | CONFLICT | LEGACY_SDK | 762 / 4 | 590 |  |
| arm64-qemu-virtio-keyboard | CONFLICT | LEGACY_SDK | 760 / 13 | 585 |  |
| arm64-real-shell-core | CONFLICT | LEGACY_SDK | 772 / 3 | 587 |  |
| arm64-stage0-no-interrupts | CONFLICT | LEGACY_SDK | 777 / 1 | 587 |  |
| arm64-stage0-no-scheduler | CONFLICT | LEGACY_SDK | 778 / 2 | 587 |  |
| arm64-stage1-framebuffer-text | CONFLICT | LEGACY_SDK | 774 / 1 | 587 |  |
| arm64-standard-console-probe | CONFLICT | LEGACY_SDK | 773 / 1 | 587 |  |
| arm64-vl805-hid-keyboard | CONFLICT | LEGACY_SDK | 766 / 4 | 590 |  |
| arm64-xhci-probe | CONFLICT | LEGACY_SDK | 770 / 3 | 588 |  |
| arm64-xhci-stage1-reset | CONFLICT | LEGACY_SDK | 764 / 3 | 589 |  |
| arm64-xhci-stage1b-mailbox-reset | CONFLICT | LEGACY_SDK | 763 / 2 | 589 |  |
| arm64-xhci-stage1d-lowmailbox | CONFLICT | LEGACY_SDK | 761 / 3 | 590 |  |
| boot-to-terminal-by-default | CONFLICT | LEGACY_SDK | 781 / 1 | 587 |  |
| build-launcher-menu | CONFLICT | LEGACY_SDK | 780 / 1 | 587 |  |
| central-app-registry | CONFLICT | LEGACY_SDK | 795 / 3 | 582 |  |
| cosmos-3.0.85-arm64-coherence | CONFLICT | LEGACY_SDK | 754 / 4 | 582 |  |
| disable-network-stack | CONFLICT | LEGACY_SDK | 782 / 1 | 587 |  |
| drivers/d1-device-pci | CONFLICT | LEGACY_SDK | 628 / 161 | 939 | [#70](https://github.com/dwierzbicki11/ZOnderqOS/pull/70) |
| drivers/d1-mainline | CONFLICT | LEGACY_SDK | 371 / 7 | 258 | [#103](https://github.com/dwierzbicki11/ZOnderqOS/pull/103) |
| drivers/d2-ahci-nvme | CONFLICT | LEGACY_SDK | 628 / 204 | 953 | [#71](https://github.com/dwierzbicki11/ZOnderqOS/pull/71) |
| drivers/d2-mainline-storage-foundation | CONFLICT | LEGACY_SDK | 371 / 12 | 266 | [#104](https://github.com/dwierzbicki11/ZOnderqOS/pull/104) |
| feat/3094-fullgui-ram-root | CONFLICT | KERNEL_OR_BUILD | 157 / 11 | 82 |  |
| feat/3094-native-resolution-normal-gui | CONFLICT | KERNEL_OR_BUILD | 158 / 10 | 85 |  |
| feat/3094-readonly-desktop-usb | CONFLICT | KERNEL_OR_BUILD | 159 / 6 | 89 |  |
| feat/3094-smp-stage9a-timer-readiness | CONFLICT | KERNEL_OR_BUILD | 151 / 5 | 63 |  |
| feat/3094-smp-stage9b-periodic-timer | CONFLICT | KERNEL_OR_BUILD | 150 / 6 | 59 |  |
| feat/3094-smp-topology-stress | CONFLICT | KERNEL_OR_BUILD | 153 / 4 | 70 |  |
| feature/3094-animated-boot-splash | CONFLICT | KERNEL_OR_BUILD | 145 / 6 | 41 |  |
| feature/3094-cursor-damage-fastpath | CONFLICT | KERNEL_OR_BUILD | 141 / 6 | 20 |  |
| feature/3094-modern-rounded-ui | CONFLICT | KERNEL_OR_BUILD | 144 / 11 | 33 |  |
| feature/3094-one-command-usb-updater | CONFLICT | KERNEL_OR_BUILD | 140 / 5 | 20 |  |
| feature/3094-responsive-app-typography | CONFLICT | KERNEL_OR_BUILD | 142 / 13 | 22 |  |
| feature/3094-ui-polish-session-panels | CONFLICT | KERNEL_OR_BUILD | 143 / 8 | 27 |  |
| feature/boot-session-health | CONFLICT | LEGACY_SDK | 523 / 1 | 282 |  |
| feature/configurable-services | CONFLICT | LEGACY_SDK | 302 / 8 | 206 |  |
| feature/coreutils | CONFLICT | LEGACY_SDK | 519 / 1 | 269 |  |
| feature/devfs-console | CONFLICT | LEGACY_SDK | 367 / 10 | 243 |  |
| feature/device-manager-binding | CONFLICT | LEGACY_SDK | 338 / 10 | 233 | [#110](https://github.com/dwierzbicki11/ZOnderqOS/pull/110) |
| feature/df-mount-usage | CONFLICT | LEGACY_SDK | 338 / 5 | 228 |  |
| feature/dmesg | CONFLICT | LEGACY_SDK | 367 / 2 | 246 |  |
| feature/driver-manager-runtime | CONFLICT | LEGACY_SDK | 302 / 6 | 215 | [#118](https://github.com/dwierzbicki11/ZOnderqOS/pull/118) |
| feature/file-clipboard-hardening | CONFLICT | LEGACY_SDK | 518 / 2 | 269 | [#94](https://github.com/dwierzbicki11/ZOnderqOS/pull/94) |
| feature/hardware-inspector-foundation | CONFLICT | LEGACY_SDK | 528 / 10 | 304 |  |
| feature/package-manager-foundation | CONFLICT | LEGACY_SDK | 527 / 1 | 298 |  |
| feature/persistent-crash-report | CONFLICT | LEGACY_SDK | 524 / 1 | 288 |  |
| feature/process-signals | CONFLICT | LEGACY_SDK | 367 / 4 | 246 | [#107](https://github.com/dwierzbicki11/ZOnderqOS/pull/107) |
| feature/process-signals-runtime | CONFLICT | LEGACY_SDK | 302 / 6 | 209 | [#119](https://github.com/dwierzbicki11/ZOnderqOS/pull/119) |
| feature/services-main-v2 | CONFLICT | LEGACY_SDK | 335 / 11 | 223 |  |
| feature/settings-hardening | CONFLICT | LEGACY_SDK | 525 / 1 | 292 |  |
| feature/shell-session | CONFLICT | LEGACY_SDK | 520 / 3 | 271 |  |
| feature/system-basics | CONFLICT | LEGACY_SDK | 517 / 4 | 268 |  |
| feature/system-doctor | CONFLICT | LEGACY_SDK | 526 / 1 | 294 |  |
| finish-incomplete-features | CONFLICT | LEGACY_SDK | 797 / 6 | 583 |  |
| fix-arm64-diagnostics-compat | CONFLICT | LEGACY_SDK | 756 / 1 | 582 |  |
| fix-arm64-imageviewer-bitmap-size | CONFLICT | LEGACY_SDK | 755 / 1 | 582 |  |
| fix-arm64-qemu-address-space | CONFLICT | LEGACY_SDK | 758 / 1 | 585 |  |
| fix-arm64-shell-symbol | CONFLICT | LEGACY_SDK | 771 / 2 | 587 |  |
| fix-arm64-smoke-compile-round2 | CONFLICT | LEGACY_SDK | 783 / 2 | 586 |  |
| fix-arm64-usb-copy-cleanup | CONFLICT | LEGACY_SDK | 779 / 1 | 587 |  |
| fix-cosmos-api-build-round2 | CONFLICT | LEGACY_SDK | 789 / 6 | 584 |  |
| fix-cosmos-build-round3 | CONFLICT | LEGACY_SDK | 788 / 4 | 583 |  |
| fix-cosmos-build-round4 | CONFLICT | LEGACY_SDK | 787 / 2 | 584 |  |
| fix-cosmos-gen3-api-build | CONFLICT | LEGACY_SDK | 792 / 5 | 583 |  |
| fix-crossarch-taskmanager-build | CONFLICT | LEGACY_SDK | 749 / 7 | 368 |  |
| fix-launcher-main-sync | CONFLICT | LEGACY_SDK | 752 / 1 | 583 |  |
| fix/3094-ci-entrypoint-guard | CONFLICT | KERNEL_OR_BUILD | 154 / 14 | 71 |  |
| fix/3094-laptop-1366x768-limine | CONFLICT | KERNEL_OR_BUILD | 147 / 4 | 47 |  |
| fix/3094-local-matching-sdk-build | CONFLICT | KERNEL_OR_BUILD | 148 / 7 | 50 |  |
| fix/3094-one-usb-safe-iso-update | CONFLICT | KERNEL_OR_BUILD | 146 / 4 | 45 |  |
| fix/3094-usb-mouse-gui | CONFLICT | KERNEL_OR_BUILD | 160 / 6 | 90 |  |
| fix/arm64-patched-runtime | CONFLICT | LEGACY_SDK | 406 / 4 | 254 | [#99](https://github.com/dwierzbicki11/ZOnderqOS/pull/99) |
| fix/package-copy-byte-limit | CONFLICT | LEGACY_SDK | 516 / 12 | 262 | [#96](https://github.com/dwierzbicki11/ZOnderqOS/pull/96) |
| fix/process-cancellation-lock | CONFLICT | LEGACY_SDK | 516 / 1 | 265 | [#95](https://github.com/dwierzbicki11/ZOnderqOS/pull/95) |
| fix/x64-patched-cosmos-runtime | CONFLICT | LEGACY_SDK | 710 / 4 | 365 |  |
| global-command-palette | CONFLICT | LEGACY_SDK | 793 / 2 | 582 |  |
| network/stage1-nic-link | CONFLICT | LEGACY_SDK | 628 / 114 | 937 | [#66](https://github.com/dwierzbicki11/ZOnderqOS/pull/66) |
| process/vmm-foundation | CONFLICT | LEGACY_SDK | 628 / 11 | 905 | [#67](https://github.com/dwierzbicki11/ZOnderqOS/pull/67) |
| qemu-hardware-matrix | CONFLICT | LEGACY_SDK | 750 / 2 | 367 |  |
| recovery-safe-mode | CONFLICT | LEGACY_SDK | 792 / 3 | 581 |  |
| recovery-safe-mode-v2 | CONFLICT | LEGACY_SDK | 791 / 5 | 582 |  |
| refactor-platform-architectures | CONFLICT | LEGACY_SDK | 748 / 16 | 361 |  |
| repo-wide-cosmos-refactor | CONFLICT | LEGACY_SDK | 751 / 15 | 367 |  |
| rpi4-arm64-boot-smoke | CONFLICT | LEGACY_SDK | 785 / 3 | 585 |  |
| rpi4-limine-gop-fix | CONFLICT | LEGACY_SDK | 786 / 1 | 584 |  |
| shell-tab-completion | CONFLICT | LEGACY_SDK | 796 / 6 | 582 |  |
| shell/bound-command-parser | CONFLICT | LEGACY_SDK | 293 / 1 | 182 |  |
| shell/bounded-alias-listing | CONFLICT | LEGACY_SDK | 297 / 2 | 186 |  |
| smt-stage2-dense-topology | CONFLICT | LEGACY_SDK | 710 / 9 | 363 |  |
| smt-stage3-ap-bringup | CONFLICT | LEGACY_SDK | 710 / 9 | 363 |  |
| smt/stage-2-dense-cpu-map | CONFLICT | LEGACY_SDK | 710 / 9 | 364 |  |
| system-logger-kernel-panic | CONFLICT | LEGACY_SDK | 794 / 8 | 582 |  |
| task-manager-telemetry | CONFLICT | LEGACY_SDK | 759 / 2 | 585 |  |
| test/3094-persistent-disk-qemu | CONFLICT | KERNEL_OR_BUILD | 156 / 8 | 78 |  |

## Largest pending deltas

- drivers/d2-ahci-nvme: .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml, .github/workflows/coreutils.yml, .github/workflows/cosmos-3094-local-build-guard.yml, .github/workflows/cosmos-3094-real-qemu.yml
- drivers/d1-device-pci: .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml, .github/workflows/coreutils.yml, .github/workflows/cosmos-3094-local-build-guard.yml, .github/workflows/cosmos-3094-real-qemu.yml
- network/stage1-nic-link: .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml, .github/workflows/coreutils.yml, .github/workflows/cosmos-3094-local-build-guard.yml, .github/workflows/cosmos-3094-real-qemu.yml
- process/vmm-foundation: .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml, .github/workflows/coreutils.yml, .github/workflows/cosmos-3094-local-build-guard.yml, .github/workflows/cosmos-3094-real-qemu.yml
- arm64-native-xhci-stage1: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- arm64-vl805-hid-keyboard: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- arm64-xhci-stage1d-lowmailbox: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- arm64-xhci-stage1-reset: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- arm64-xhci-stage1b-mailbox-reset: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- arm64-bcm2711-pci-xhci-probe: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- arm64-cosmos-devicemapper-xhci: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- arm64-irq-gic-reenable-test: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- arm64-xhci-probe: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- arm64-direct-framebuffer-probe: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- arm64-disable-qemu-uart: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- arm64-real-shell-core: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- arm64-stage0-no-interrupts: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- arm64-stage0-no-scheduler: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- arm64-stage1-framebuffer-text: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- arm64-standard-console-probe: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- boot-to-terminal-by-default: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- build-launcher-menu: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- disable-network-stack: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- fix-arm64-shell-symbol: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- fix-arm64-usb-copy-cleanup: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- fix-arm64-smoke-compile-round2: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- arm64-qemu-virtio-keyboard: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- fix-arm64-qemu-address-space: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- rpi4-arm64-boot-smoke: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- task-manager-telemetry: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/acl-hardening.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml

## Safety gates

Do not automatically merge changes touching the kernel, managed SMP, OrionGC, disks, security, network or hardware drivers. Require matching Cosmos 3.0.94 packages, builds, QEMU tests and relevant physical validation. Do not mistake contract tests for hardware proof.
