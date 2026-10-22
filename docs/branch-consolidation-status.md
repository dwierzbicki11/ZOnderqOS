# ZonderqOS — branch inventory

Generated UTC: 2026-10-22T16:08:48.496911+00:00
main commit: 495443c282b12f23ec38600a135a8a3e64a852fe
Canonical SDK/runtime: Cosmos 3.0.94 (no downgrade).

This is a read-only inventory. It does not merge, delete, or certify SMP.

## Summary

Non-main branches: 123

- CONFLICT: 106
- NEEDS_PORT_AND_TEST: 17

## Classification

- HISTORY_IN_MAIN: branch tip already reachable from main
- SAME_TREE: all tracked content matches main, but history differs
- MERGE_NO_CHANGE: clean merge changes no main files
- NEEDS_PORT_AND_TEST: would change main; requires gated tests
- CONFLICT: cannot merge automatically; selective port required

## Remaining branches

| Branch | State | Risk | Behind / Ahead | Files | PR |
|---|---|---|---:|---:|---|
| ai/usb-1-testy | NEEDS_PORT_AND_TEST | LEGACY_SDK | 265 / 2 | 2 |  |
| ai/usb-2-smp | NEEDS_PORT_AND_TEST | LEGACY_SDK | 265 / 2 | 3 |  |
| ai/usb-3-cosmos3094 | NEEDS_PORT_AND_TEST | LEGACY_SDK | 265 / 1 | 2 | [#152](https://github.com/dwierzbicki11/ZOnderqOS/pull/152) |
| ai/usb-4-shell | NEEDS_PORT_AND_TEST | LEGACY_SDK | 265 / 2 | 3 |  |
| ai/usb-5-gui | NEEDS_PORT_AND_TEST | LEGACY_SDK | 265 / 2 | 5 |  |
| arm64-diagnostics-fallback | NEEDS_PORT_AND_TEST | LEGACY_SDK | 778 / 4 | 2 |  |
| arm64-xhci-usb-keyboard-shell | NEEDS_PORT_AND_TEST | LEGACY_SDK | 791 / 3 | 3 |  |
| copilot/expand-system-development | NEEDS_PORT_AND_TEST | LEGACY_SDK | 286 / 1 | 4 | [#144](https://github.com/dwierzbicki11/ZOnderqOS/pull/144) |
| feat/3094-smp-stage12-managed-ap-proof | NEEDS_PORT_AND_TEST | KERNEL_OR_BUILD | 173 / 3 | 5 | [#170](https://github.com/dwierzbicki11/ZOnderqOS/pull/170) |
| feature/log-query | NEEDS_PORT_AND_TEST | LEGACY_SDK | 548 / 6 | 5 | [#88](https://github.com/dwierzbicki11/ZOnderqOS/pull/88) |
| feature/system-installer-foundation | NEEDS_PORT_AND_TEST | LEGACY_SDK | 326 / 4 | 4 |  |
| fix/3094-cs0016-generator-compile-gates | NEEDS_PORT_AND_TEST | APP_OR_TOOL | 162 / 7 | 3 |  |
| fix/3094-smt-stage23-ci-preflight | NEEDS_PORT_AND_TEST | KERNEL_OR_BUILD | 176 / 2 | 2 | [#165](https://github.com/dwierzbicki11/ZOnderqOS/pull/165) |
| gui-modern3 | NEEDS_PORT_AND_TEST | LEGACY_SDK | 1045 / 1 | 1 |  |
| kernel/ring3-syscall-foundation | NEEDS_PORT_AND_TEST | LEGACY_SDK | 653 / 9 | 4 | [#68](https://github.com/dwierzbicki11/ZOnderqOS/pull/68) |
| smp/runtime-all-cpus | NEEDS_PORT_AND_TEST | LEGACY_SDK | 668 / 3 | 3 |  |
| smt/night-2026-09-18-work-proof | NEEDS_PORT_AND_TEST | LEGACY_SDK | 696 / 3 | 1 |  |
| ai/usb-10-testy | CONFLICT | LEGACY_SDK | 265 / 3 | 140 |  |
| ai/usb-12-shell | CONFLICT | LEGACY_SDK | 265 / 3 | 140 |  |
| ai/usb-13-gui | CONFLICT | LEGACY_SDK | 265 / 3 | 140 |  |
| ai/usb-6-testy | CONFLICT | LEGACY_SDK | 265 / 3 | 139 |  |
| ai/usb-7-smp | CONFLICT | LEGACY_SDK | 265 / 3 | 141 |  |
| ai/usb-8-shell | CONFLICT | LEGACY_SDK | 265 / 3 | 141 |  |
| ai/usb-9-gui | CONFLICT | LEGACY_SDK | 265 / 3 | 143 |  |
| arm64-bcm2711-pci-xhci-probe | CONFLICT | LEGACY_SDK | 794 / 4 | 600 |  |
| arm64-cosmos-devicemapper-xhci | CONFLICT | LEGACY_SDK | 792 / 3 | 600 |  |
| arm64-direct-framebuffer-probe | CONFLICT | LEGACY_SDK | 801 / 2 | 599 |  |
| arm64-disable-qemu-uart | CONFLICT | LEGACY_SDK | 800 / 1 | 599 |  |
| arm64-full-desktop-parity | CONFLICT | LEGACY_SDK | 782 / 7 | 594 |  |
| arm64-irq-gic-reenable-test | CONFLICT | LEGACY_SDK | 793 / 2 | 600 |  |
| arm64-native-xhci-stage1 | CONFLICT | LEGACY_SDK | 787 / 4 | 602 |  |
| arm64-qemu-virtio-keyboard | CONFLICT | LEGACY_SDK | 785 / 13 | 597 |  |
| arm64-real-shell-core | CONFLICT | LEGACY_SDK | 797 / 3 | 599 |  |
| arm64-stage0-no-interrupts | CONFLICT | LEGACY_SDK | 802 / 1 | 599 |  |
| arm64-stage0-no-scheduler | CONFLICT | LEGACY_SDK | 803 / 2 | 599 |  |
| arm64-stage1-framebuffer-text | CONFLICT | LEGACY_SDK | 799 / 1 | 599 |  |
| arm64-standard-console-probe | CONFLICT | LEGACY_SDK | 798 / 1 | 599 |  |
| arm64-vl805-hid-keyboard | CONFLICT | LEGACY_SDK | 791 / 4 | 602 |  |
| arm64-xhci-probe | CONFLICT | LEGACY_SDK | 795 / 3 | 600 |  |
| arm64-xhci-stage1-reset | CONFLICT | LEGACY_SDK | 789 / 3 | 601 |  |
| arm64-xhci-stage1b-mailbox-reset | CONFLICT | LEGACY_SDK | 788 / 2 | 601 |  |
| arm64-xhci-stage1d-lowmailbox | CONFLICT | LEGACY_SDK | 786 / 3 | 602 |  |
| boot-to-terminal-by-default | CONFLICT | LEGACY_SDK | 806 / 1 | 599 |  |
| build-launcher-menu | CONFLICT | LEGACY_SDK | 805 / 1 | 599 |  |
| central-app-registry | CONFLICT | LEGACY_SDK | 820 / 3 | 594 |  |
| cosmos-3.0.85-arm64-coherence | CONFLICT | LEGACY_SDK | 779 / 4 | 594 |  |
| disable-network-stack | CONFLICT | LEGACY_SDK | 807 / 1 | 599 |  |
| drivers/d1-device-pci | CONFLICT | LEGACY_SDK | 653 / 161 | 951 | [#70](https://github.com/dwierzbicki11/ZOnderqOS/pull/70) |
| drivers/d1-mainline | CONFLICT | LEGACY_SDK | 396 / 7 | 270 | [#103](https://github.com/dwierzbicki11/ZOnderqOS/pull/103) |
| drivers/d2-ahci-nvme | CONFLICT | LEGACY_SDK | 653 / 204 | 965 | [#71](https://github.com/dwierzbicki11/ZOnderqOS/pull/71) |
| drivers/d2-mainline-storage-foundation | CONFLICT | LEGACY_SDK | 396 / 12 | 278 | [#104](https://github.com/dwierzbicki11/ZOnderqOS/pull/104) |
| feat/3094-fullgui-ram-root | CONFLICT | KERNEL_OR_BUILD | 182 / 11 | 95 |  |
| feat/3094-native-resolution-normal-gui | CONFLICT | KERNEL_OR_BUILD | 183 / 10 | 98 |  |
| feat/3094-readonly-desktop-usb | CONFLICT | KERNEL_OR_BUILD | 184 / 6 | 102 |  |
| feat/3094-smp-stage9a-timer-readiness | CONFLICT | KERNEL_OR_BUILD | 176 / 5 | 76 |  |
| feat/3094-smp-stage9b-periodic-timer | CONFLICT | KERNEL_OR_BUILD | 175 / 6 | 72 |  |
| feat/3094-smp-topology-stress | CONFLICT | KERNEL_OR_BUILD | 178 / 4 | 83 |  |
| feature/3094-animated-boot-splash | CONFLICT | KERNEL_OR_BUILD | 170 / 6 | 56 |  |
| feature/3094-cursor-damage-fastpath | CONFLICT | KERNEL_OR_BUILD | 166 / 6 | 35 |  |
| feature/3094-modern-rounded-ui | CONFLICT | KERNEL_OR_BUILD | 169 / 11 | 48 |  |
| feature/3094-one-command-usb-updater | CONFLICT | KERNEL_OR_BUILD | 165 / 5 | 35 |  |
| feature/3094-responsive-app-typography | CONFLICT | KERNEL_OR_BUILD | 167 / 13 | 37 |  |
| feature/3094-ui-polish-session-panels | CONFLICT | KERNEL_OR_BUILD | 168 / 8 | 42 |  |
| feature/boot-session-health | CONFLICT | LEGACY_SDK | 548 / 1 | 294 |  |
| feature/configurable-services | CONFLICT | LEGACY_SDK | 327 / 8 | 218 |  |
| feature/coreutils | CONFLICT | LEGACY_SDK | 544 / 1 | 281 |  |
| feature/devfs-console | CONFLICT | LEGACY_SDK | 392 / 10 | 255 |  |
| feature/device-manager-binding | CONFLICT | LEGACY_SDK | 363 / 10 | 245 | [#110](https://github.com/dwierzbicki11/ZOnderqOS/pull/110) |
| feature/df-mount-usage | CONFLICT | LEGACY_SDK | 363 / 5 | 240 |  |
| feature/dmesg | CONFLICT | LEGACY_SDK | 392 / 2 | 258 |  |
| feature/driver-manager-runtime | CONFLICT | LEGACY_SDK | 327 / 6 | 227 | [#118](https://github.com/dwierzbicki11/ZOnderqOS/pull/118) |
| feature/file-clipboard-hardening | CONFLICT | LEGACY_SDK | 543 / 2 | 281 | [#94](https://github.com/dwierzbicki11/ZOnderqOS/pull/94) |
| feature/hardware-inspector-foundation | CONFLICT | LEGACY_SDK | 553 / 10 | 316 |  |
| feature/package-manager-foundation | CONFLICT | LEGACY_SDK | 552 / 1 | 310 |  |
| feature/persistent-crash-report | CONFLICT | LEGACY_SDK | 549 / 1 | 300 |  |
| feature/process-signals | CONFLICT | LEGACY_SDK | 392 / 4 | 258 | [#107](https://github.com/dwierzbicki11/ZOnderqOS/pull/107) |
| feature/process-signals-runtime | CONFLICT | LEGACY_SDK | 327 / 6 | 221 | [#119](https://github.com/dwierzbicki11/ZOnderqOS/pull/119) |
| feature/services-main-v2 | CONFLICT | LEGACY_SDK | 360 / 11 | 235 |  |
| feature/settings-hardening | CONFLICT | LEGACY_SDK | 550 / 1 | 304 |  |
| feature/shell-session | CONFLICT | LEGACY_SDK | 545 / 3 | 283 |  |
| feature/system-basics | CONFLICT | LEGACY_SDK | 542 / 4 | 280 |  |
| feature/system-doctor | CONFLICT | LEGACY_SDK | 551 / 1 | 306 |  |
| finish-incomplete-features | CONFLICT | LEGACY_SDK | 822 / 6 | 595 |  |
| fix-arm64-diagnostics-compat | CONFLICT | LEGACY_SDK | 781 / 1 | 594 |  |
| fix-arm64-imageviewer-bitmap-size | CONFLICT | LEGACY_SDK | 780 / 1 | 594 |  |
| fix-arm64-qemu-address-space | CONFLICT | LEGACY_SDK | 783 / 1 | 597 |  |
| fix-arm64-shell-symbol | CONFLICT | LEGACY_SDK | 796 / 2 | 599 |  |
| fix-arm64-smoke-compile-round2 | CONFLICT | LEGACY_SDK | 808 / 2 | 598 |  |
| fix-arm64-usb-copy-cleanup | CONFLICT | LEGACY_SDK | 804 / 1 | 599 |  |
| fix-cosmos-api-build-round2 | CONFLICT | LEGACY_SDK | 814 / 6 | 596 |  |
| fix-cosmos-build-round3 | CONFLICT | LEGACY_SDK | 813 / 4 | 595 |  |
| fix-cosmos-build-round4 | CONFLICT | LEGACY_SDK | 812 / 2 | 596 |  |
| fix-cosmos-gen3-api-build | CONFLICT | LEGACY_SDK | 817 / 5 | 595 |  |
| fix-crossarch-taskmanager-build | CONFLICT | LEGACY_SDK | 774 / 7 | 380 |  |
| fix-launcher-main-sync | CONFLICT | LEGACY_SDK | 777 / 1 | 595 |  |
| fix/3094-ci-entrypoint-guard | CONFLICT | KERNEL_OR_BUILD | 179 / 14 | 84 |  |
| fix/3094-laptop-1366x768-limine | CONFLICT | KERNEL_OR_BUILD | 172 / 4 | 62 |  |
| fix/3094-local-matching-sdk-build | CONFLICT | KERNEL_OR_BUILD | 173 / 7 | 63 |  |
| fix/3094-one-usb-safe-iso-update | CONFLICT | KERNEL_OR_BUILD | 171 / 4 | 60 |  |
| fix/3094-usb-mouse-gui | CONFLICT | KERNEL_OR_BUILD | 185 / 6 | 103 |  |
| fix/arm64-patched-runtime | CONFLICT | LEGACY_SDK | 431 / 4 | 266 | [#99](https://github.com/dwierzbicki11/ZOnderqOS/pull/99) |
| fix/package-copy-byte-limit | CONFLICT | LEGACY_SDK | 541 / 12 | 274 | [#96](https://github.com/dwierzbicki11/ZOnderqOS/pull/96) |
| fix/process-cancellation-lock | CONFLICT | LEGACY_SDK | 541 / 1 | 277 | [#95](https://github.com/dwierzbicki11/ZOnderqOS/pull/95) |
| fix/x64-patched-cosmos-runtime | CONFLICT | LEGACY_SDK | 735 / 4 | 377 |  |
| global-command-palette | CONFLICT | LEGACY_SDK | 818 / 2 | 594 |  |
| network/stage1-nic-link | CONFLICT | LEGACY_SDK | 653 / 114 | 949 | [#66](https://github.com/dwierzbicki11/ZOnderqOS/pull/66) |
| process/vmm-foundation | CONFLICT | LEGACY_SDK | 653 / 11 | 917 | [#67](https://github.com/dwierzbicki11/ZOnderqOS/pull/67) |
| qemu-hardware-matrix | CONFLICT | LEGACY_SDK | 775 / 2 | 379 |  |
| recovery-safe-mode | CONFLICT | LEGACY_SDK | 817 / 3 | 593 |  |
| recovery-safe-mode-v2 | CONFLICT | LEGACY_SDK | 816 / 5 | 594 |  |
| refactor-platform-architectures | CONFLICT | LEGACY_SDK | 773 / 16 | 373 |  |
| repo-wide-cosmos-refactor | CONFLICT | LEGACY_SDK | 776 / 15 | 379 |  |
| rpi4-arm64-boot-smoke | CONFLICT | LEGACY_SDK | 810 / 3 | 597 |  |
| rpi4-limine-gop-fix | CONFLICT | LEGACY_SDK | 811 / 1 | 596 |  |
| shell-tab-completion | CONFLICT | LEGACY_SDK | 821 / 6 | 594 |  |
| shell/bound-command-parser | CONFLICT | LEGACY_SDK | 318 / 1 | 194 |  |
| shell/bounded-alias-listing | CONFLICT | LEGACY_SDK | 322 / 2 | 198 |  |
| smt-stage2-dense-topology | CONFLICT | LEGACY_SDK | 735 / 9 | 375 |  |
| smt-stage3-ap-bringup | CONFLICT | LEGACY_SDK | 735 / 9 | 375 |  |
| smt/stage-2-dense-cpu-map | CONFLICT | LEGACY_SDK | 735 / 9 | 376 |  |
| system-logger-kernel-panic | CONFLICT | LEGACY_SDK | 819 / 8 | 594 |  |
| task-manager-telemetry | CONFLICT | LEGACY_SDK | 784 / 2 | 597 |  |
| test/3094-persistent-disk-qemu | CONFLICT | KERNEL_OR_BUILD | 181 / 8 | 91 |  |

## Largest pending deltas

- drivers/d2-ahci-nvme: .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- drivers/d1-device-pci: .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- network/stage1-nic-link: .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- process/vmm-foundation: .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml, .github/workflows/branch-consolidation.yml, .github/workflows/branch-inventory.yml, .github/workflows/config-backup.yml
- arm64-native-xhci-stage1: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- arm64-vl805-hid-keyboard: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- arm64-xhci-stage1d-lowmailbox: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- arm64-xhci-stage1-reset: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- arm64-xhci-stage1b-mailbox-reset: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- arm64-bcm2711-pci-xhci-probe: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- arm64-cosmos-devicemapper-xhci: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- arm64-irq-gic-reenable-test: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- arm64-xhci-probe: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- arm64-direct-framebuffer-probe: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- arm64-disable-qemu-uart: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- arm64-real-shell-core: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- arm64-stage0-no-interrupts: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- arm64-stage0-no-scheduler: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- arm64-stage1-framebuffer-text: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- arm64-standard-console-probe: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- boot-to-terminal-by-default: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- build-launcher-menu: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- disable-network-stack: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- fix-arm64-shell-symbol: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- fix-arm64-usb-copy-cleanup: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- fix-arm64-smoke-compile-round2: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- arm64-qemu-virtio-keyboard: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- fix-arm64-qemu-address-space: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- rpi4-arm64-boot-smoke: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml
- task-manager-telemetry: .editorconfig, .github/agents/agent.md, .github/agents/night-maintainer.agent.md, .github/workflows/3094-displayinfo-gate.yml, .github/workflows/3094-generated-output-gate.yml, .github/workflows/acl-hardening.yml, .github/workflows/archive-ported-branches.yml, .github/workflows/boot-session-health.yml

## Safety gates

Do not automatically merge changes touching the kernel, managed SMP, OrionGC, disks, security, network or hardware drivers. Require matching Cosmos 3.0.94 packages, builds, QEMU tests and relevant physical validation. Do not mistake contract tests for hardware proof.
