# ZonderqOS — branch inventory

Generated UTC: 2026-10-22T15:48:49.193372+00:00
main commit: 2a615da73ebfc3f735714ed14b5b2435d2998096
Canonical SDK/runtime: Cosmos 3.0.94 (no downgrade).

This is a read-only inventory. It does not merge, delete, or certify SMP.

## Summary

Non-main branches: 127

- CONFLICT: 106
- NEEDS_PORT_AND_TEST: 21

## Classification

- HISTORY_IN_MAIN: branch tip already reachable from main
- SAME_TREE: all tracked content matches main, but history differs
- MERGE_NO_CHANGE: clean merge changes no main files
- NEEDS_PORT_AND_TEST: would change main; requires gated tests
- CONFLICT: cannot merge automatically; selective port required

## Remaining branches

| Branch | State | Risk | Behind / Ahead | Files | PR |
|---|---|---|---:|---:|---|
| ai/usb-1-testy | NEEDS_PORT_AND_TEST | LEGACY_SDK | 242 / 2 | 2 |  |
| ai/usb-2-smp | NEEDS_PORT_AND_TEST | LEGACY_SDK | 242 / 2 | 3 |  |
| ai/usb-3-cosmos3094 | NEEDS_PORT_AND_TEST | LEGACY_SDK | 242 / 1 | 2 | [#152](https://github.com/dwierzbicki11/ZOnderqOS/pull/152) |
| ai/usb-4-shell | NEEDS_PORT_AND_TEST | LEGACY_SDK | 242 / 2 | 3 |  |
| ai/usb-5-gui | NEEDS_PORT_AND_TEST | LEGACY_SDK | 242 / 2 | 5 |  |
| arm64-diagnostics-fallback | NEEDS_PORT_AND_TEST | LEGACY_SDK | 755 / 4 | 2 |  |
| arm64-xhci-usb-keyboard-shell | NEEDS_PORT_AND_TEST | LEGACY_SDK | 768 / 3 | 3 |  |
| cleanup/3094-cs0016-build-fix | NEEDS_PORT_AND_TEST | KERNEL_OR_BUILD | 1 / 3 | 3 | [#186](https://github.com/dwierzbicki11/ZOnderqOS/pull/186) |
| cleanup/3094-displayinfo-port | NEEDS_PORT_AND_TEST | APP_OR_TOOL | 1 / 3 | 3 | [#185](https://github.com/dwierzbicki11/ZOnderqOS/pull/185) |
| copilot/expand-system-development | NEEDS_PORT_AND_TEST | LEGACY_SDK | 263 / 1 | 4 | [#144](https://github.com/dwierzbicki11/ZOnderqOS/pull/144) |
| feat/3094-smp-full-managed-port-gates | NEEDS_PORT_AND_TEST | KERNEL_OR_BUILD | 152 / 3 | 3 | [#168](https://github.com/dwierzbicki11/ZOnderqOS/pull/168) |
| feat/3094-smp-stage12-managed-ap-proof | NEEDS_PORT_AND_TEST | KERNEL_OR_BUILD | 150 / 3 | 5 | [#170](https://github.com/dwierzbicki11/ZOnderqOS/pull/170) |
| feature/log-query | NEEDS_PORT_AND_TEST | LEGACY_SDK | 525 / 6 | 5 | [#88](https://github.com/dwierzbicki11/ZOnderqOS/pull/88) |
| feature/system-installer-foundation | NEEDS_PORT_AND_TEST | LEGACY_SDK | 303 / 4 | 4 |  |
| fix/3094-cs0016-generator-compile-gates | NEEDS_PORT_AND_TEST | KERNEL_OR_BUILD | 139 / 7 | 5 |  |
| fix/3094-display-hardware-diagnostics | NEEDS_PORT_AND_TEST | APP_OR_TOOL | 147 / 5 | 4 | [#174](https://github.com/dwierzbicki11/ZOnderqOS/pull/174) |
| fix/3094-smt-stage23-ci-preflight | NEEDS_PORT_AND_TEST | KERNEL_OR_BUILD | 153 / 2 | 2 | [#165](https://github.com/dwierzbicki11/ZOnderqOS/pull/165) |
| gui-modern3 | NEEDS_PORT_AND_TEST | LEGACY_SDK | 1022 / 1 | 1 |  |
| kernel/ring3-syscall-foundation | NEEDS_PORT_AND_TEST | LEGACY_SDK | 630 / 9 | 4 | [#68](https://github.com/dwierzbicki11/ZOnderqOS/pull/68) |
| smp/runtime-all-cpus | NEEDS_PORT_AND_TEST | LEGACY_SDK | 645 / 3 | 3 |  |
| smt/night-2026-09-18-work-proof | NEEDS_PORT_AND_TEST | LEGACY_SDK | 673 / 3 | 1 |  |
| ai/usb-10-testy | CONFLICT | LEGACY_SDK | 242 / 3 | 129 |  |
| ai/usb-12-shell | CONFLICT | LEGACY_SDK | 242 / 3 | 129 |  |
| ai/usb-13-gui | CONFLICT | LEGACY_SDK | 242 / 3 | 129 |  |
| ai/usb-6-testy | CONFLICT | LEGACY_SDK | 242 / 3 | 128 |  |
| ai/usb-7-smp | CONFLICT | LEGACY_SDK | 242 / 3 | 130 |  |
| ai/usb-8-shell | CONFLICT | LEGACY_SDK | 242 / 3 | 130 |  |
| ai/usb-9-gui | CONFLICT | LEGACY_SDK | 242 / 3 | 132 |  |
| arm64-bcm2711-pci-xhci-probe | CONFLICT | LEGACY_SDK | 771 / 4 | 589 |  |
| arm64-cosmos-devicemapper-xhci | CONFLICT | LEGACY_SDK | 769 / 3 | 589 |  |
| arm64-direct-framebuffer-probe | CONFLICT | LEGACY_SDK | 778 / 2 | 588 |  |
| arm64-disable-qemu-uart | CONFLICT | LEGACY_SDK | 777 / 1 | 588 |  |
| arm64-full-desktop-parity | CONFLICT | LEGACY_SDK | 759 / 7 | 583 |  |
| arm64-irq-gic-reenable-test | CONFLICT | LEGACY_SDK | 770 / 2 | 589 |  |
| arm64-native-xhci-stage1 | CONFLICT | LEGACY_SDK | 764 / 4 | 591 |  |
| arm64-qemu-virtio-keyboard | CONFLICT | LEGACY_SDK | 762 / 13 | 586 |  |
| arm64-real-shell-core | CONFLICT | LEGACY_SDK | 774 / 3 | 588 |  |
| arm64-stage0-no-interrupts | CONFLICT | LEGACY_SDK | 779 / 1 | 588 |  |
| arm64-stage0-no-scheduler | CONFLICT | LEGACY_SDK | 780 / 2 | 588 |  |
| arm64-stage1-framebuffer-text | CONFLICT | LEGACY_SDK | 776 / 1 | 588 |  |
| arm64-standard-console-probe | CONFLICT | LEGACY_SDK | 775 / 1 | 588 |  |
| arm64-vl805-hid-keyboard | CONFLICT | LEGACY_SDK | 768 / 4 | 591 |  |
| arm64-xhci-probe | CONFLICT | LEGACY_SDK | 772 / 3 | 589 |  |
| arm64-xhci-stage1-reset | CONFLICT | LEGACY_SDK | 766 / 3 | 590 |  |
| arm64-xhci-stage1b-mailbox-reset | CONFLICT | LEGACY_SDK | 765 / 2 | 590 |  |
| arm64-xhci-stage1d-lowmailbox | CONFLICT | LEGACY_SDK | 763 / 3 | 591 |  |
| boot-to-terminal-by-default | CONFLICT | LEGACY_SDK | 783 / 1 | 588 |  |
| build-launcher-menu | CONFLICT | LEGACY_SDK | 782 / 1 | 588 |  |
| central-app-registry | CONFLICT | LEGACY_SDK | 797 / 3 | 583 |  |
| cosmos-3.0.85-arm64-coherence | CONFLICT | LEGACY_SDK | 756 / 4 | 583 |  |
| disable-network-stack | CONFLICT | LEGACY_SDK | 784 / 1 | 588 |  |
| drivers/d1-device-pci | CONFLICT | LEGACY_SDK | 630 / 161 | 940 | [#70](https://github.com/dwierzbicki11/ZOnderqOS/pull/70) |
| drivers/d1-mainline | CONFLICT | LEGACY_SDK | 373 / 7 | 259 | [#103](https://github.com/dwierzbicki11/ZOnderqOS/pull/103) |
| drivers/d2-ahci-nvme | CONFLICT | LEGACY_SDK | 630 / 204 | 954 | [#71](https://github.com/dwierzbicki11/ZOnderqOS/pull/71) |
| drivers/d2-mainline-storage-foundation | CONFLICT | LEGACY_SDK | 373 / 12 | 267 | [#104](https://github.com/dwierzbicki11/ZOnderqOS/pull/104) |
| feat/3094-fullgui-ram-root | CONFLICT | KERNEL_OR_BUILD | 159 / 11 | 83 |  |
| feat/3094-native-resolution-normal-gui | CONFLICT | KERNEL_OR_BUILD | 160 / 10 | 86 |  |
| feat/3094-readonly-desktop-usb | CONFLICT | KERNEL_OR_BUILD | 161 / 6 | 90 |  |
| feat/3094-smp-stage9a-timer-readiness | CONFLICT | KERNEL_OR_BUILD | 153 / 5 | 64 |  |
| feat/3094-smp-stage9b-periodic-timer | CONFLICT | KERNEL_OR_BUILD | 152 / 6 | 60 |  |
| feat/3094-smp-topology-stress | CONFLICT | KERNEL_OR_BUILD | 155 / 4 | 71 |  |
| feature/3094-animated-boot-splash | CONFLICT | KERNEL_OR_BUILD | 147 / 6 | 42 |  |
| feature/3094-cursor-damage-fastpath | CONFLICT | KERNEL_OR_BUILD | 143 / 6 | 21 |  |
| feature/3094-modern-rounded-ui | CONFLICT | KERNEL_OR_BUILD | 146 / 11 | 34 |  |
| feature/3094-one-command-usb-updater | CONFLICT | KERNEL_OR_BUILD | 142 / 5 | 21 |  |
| feature/3094-responsive-app-typography | CONFLICT | KERNEL_OR_BUILD | 144 / 13 | 23 |  |
| feature/3094-ui-polish-session-panels | CONFLICT | KERNEL_OR_BUILD | 145 / 8 | 28 |  |
| feature/boot-session-health | CONFLICT | LEGACY_SDK | 525 / 1 | 283 |  |
| feature/configurable-services | CONFLICT | LEGACY_SDK | 304 / 8 | 207 |  |
| feature/coreutils | CONFLICT | LEGACY_SDK | 521 / 1 | 270 |  |
| feature/devfs-console | CONFLICT | LEGACY_SDK | 369 / 10 | 244 |  |
| feature/device-manager-binding | CONFLICT | LEGACY_SDK | 340 / 10 | 234 | [#110](https://github.com/dwierzbicki11/ZOnderqOS/pull/110) |
| feature/df-mount-usage | CONFLICT | LEGACY_SDK | 340 / 5 | 229 |  |
| feature/dmesg | CONFLICT | LEGACY_SDK | 369 / 2 | 247 |  |
| feature/driver-manager-runtime | CONFLICT | LEGACY_SDK | 304 / 6 | 216 | [#118](https://github.com/dwierzbicki11/ZOnderqOS/pull/118) |
| feature/file-clipboard-hardening | CONFLICT | LEGACY_SDK | 520 / 2 | 270 | [#94](https://github.com/dwierzbicki11/ZOnderqOS/pull/94) |
| feature/hardware-inspector-foundation | CONFLICT | LEGACY_SDK | 530 / 10 | 305 |  |
| feature/package-manager-foundation | CONFLICT | LEGACY_SDK | 529 / 1 | 299 |  |
| feature/persistent-crash-report | CONFLICT | LEGACY_SDK | 526 / 1 | 289 |  |
| feature/process-signals | CONFLICT | LEGACY_SDK | 369 / 4 | 247 | [#107](https://github.com/dwierzbicki11/ZOnderqOS/pull/107) |
| feature/process-signals-runtime | CONFLICT | LEGACY_SDK | 304 / 6 | 210 | [#119](https://github.com/dwierzbicki11/ZOnderqOS/pull/119) |
| feature/services-main-v2 | CONFLICT | LEGACY_SDK | 337 / 11 | 224 |  |
| feature/settings-hardening | CONFLICT | LEGACY_SDK | 527 / 1 | 293 |  |
| feature/shell-session | CONFLICT | LEGACY_SDK | 522 / 3 | 272 |  |
| feature/system-basics | CONFLICT | LEGACY_SDK | 519 / 4 | 269 |  |
| feature/system-doctor | CONFLICT | LEGACY_SDK | 528 / 1 | 295 |  |
| finish-incomplete-features | CONFLICT | LEGACY_SDK | 799 / 6 | 584 |  |
| fix-arm64-diagnostics-compat | CONFLICT | LEGACY_SDK | 758 / 1 | 583 |  |
| fix-arm64-imageviewer-bitmap-size | CONFLICT | LEGACY_SDK | 757 / 1 | 583 |  |
| fix-arm64-qemu-address-space | CONFLICT | LEGACY_SDK | 760 / 1 | 586 |  |
| fix-arm64-shell-symbol | CONFLICT | LEGACY_SDK | 773 / 2 | 588 |  |
| fix-arm64-smoke-compile-round2 | CONFLICT | LEGACY_SDK | 785 / 2 | 587 |  |
| fix-arm64-usb-copy-cleanup | CONFLICT | LEGACY_SDK | 781 / 1 | 588 |  |
| fix-cosmos-api-build-round2 | CONFLICT | LEGACY_SDK | 791 / 6 | 585 |  |
| fix-cosmos-build-round3 | CONFLICT | LEGACY_SDK | 790 / 4 | 584 |  |
| fix-cosmos-build-round4 | CONFLICT | LEGACY_SDK | 789 / 2 | 585 |  |
| fix-cosmos-gen3-api-build | CONFLICT | LEGACY_SDK | 794 / 5 | 584 |  |
| fix-crossarch-taskmanager-build | CONFLICT | LEGACY_SDK | 751 / 7 | 369 |  |
| fix-launcher-main-sync | CONFLICT | LEGACY_SDK | 754 / 1 | 584 |  |
| fix/3094-ci-entrypoint-guard | CONFLICT | KERNEL_OR_BUILD | 156 / 14 | 72 |  |
| fix/3094-laptop-1366x768-limine | CONFLICT | KERNEL_OR_BUILD | 149 / 4 | 48 |  |
| fix/3094-local-matching-sdk-build | CONFLICT | KERNEL_OR_BUILD | 150 / 7 | 51 |  |
| fix/3094-one-usb-safe-iso-update | CONFLICT | KERNEL_OR_BUILD | 148 / 4 | 46 |  |
| fix/3094-usb-mouse-gui | CONFLICT | KERNEL_OR_BUILD | 162 / 6 | 91 |  |
| fix/arm64-patched-runtime | CONFLICT | LEGACY_SDK | 408 / 4 | 255 | [#99](https://github.com/dwierzbicki11/ZOnderqOS/pull/99) |
| fix/package-copy-byte-limit | CONFLICT | LEGACY_SDK | 518 / 12 | 263 | [#96](https://github.com/dwierzbicki11/ZOnderqOS/pull/96) |
| fix/process-cancellation-lock | CONFLICT | LEGACY_SDK | 518 / 1 | 266 | [#95](https://github.com/dwierzbicki11/ZOnderqOS/pull/95) |
| fix/x64-patched-cosmos-runtime | CONFLICT | LEGACY_SDK | 712 / 4 | 366 |  |
| global-command-palette | CONFLICT | LEGACY_SDK | 795 / 2 | 583 |  |
| network/stage1-nic-link | CONFLICT | LEGACY_SDK | 630 / 114 | 938 | [#66](https://github.com/dwierzbicki11/ZOnderqOS/pull/66) |
| process/vmm-foundation | CONFLICT | LEGACY_SDK | 630 / 11 | 906 | [#67](https://github.com/dwierzbicki11/ZOnderqOS/pull/67) |
| qemu-hardware-matrix | CONFLICT | LEGACY_SDK | 752 / 2 | 368 |  |
| recovery-safe-mode | CONFLICT | LEGACY_SDK | 794 / 3 | 582 |  |
| recovery-safe-mode-v2 | CONFLICT | LEGACY_SDK | 793 / 5 | 583 |  |
| refactor-platform-architectures | CONFLICT | LEGACY_SDK | 750 / 16 | 362 |  |
| repo-wide-cosmos-refactor | CONFLICT | LEGACY_SDK | 753 / 15 | 368 |  |
| rpi4-arm64-boot-smoke | CONFLICT | LEGACY_SDK | 787 / 3 | 586 |  |
| rpi4-limine-gop-fix | CONFLICT | LEGACY_SDK | 788 / 1 | 585 |  |
| shell-tab-completion | CONFLICT | LEGACY_SDK | 798 / 6 | 583 |  |
| shell/bound-command-parser | CONFLICT | LEGACY_SDK | 295 / 1 | 183 |  |
| shell/bounded-alias-listing | CONFLICT | LEGACY_SDK | 299 / 2 | 187 |  |
| smt-stage2-dense-topology | CONFLICT | LEGACY_SDK | 712 / 9 | 364 |  |
| smt-stage3-ap-bringup | CONFLICT | LEGACY_SDK | 712 / 9 | 364 |  |
| smt/stage-2-dense-cpu-map | CONFLICT | LEGACY_SDK | 712 / 9 | 365 |  |
| system-logger-kernel-panic | CONFLICT | LEGACY_SDK | 796 / 8 | 583 |  |
| task-manager-telemetry | CONFLICT | LEGACY_SDK | 761 / 2 | 586 |  |
| test/3094-persistent-disk-qemu | CONFLICT | KERNEL_OR_BUILD | 158 / 8 | 79 |  |

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
