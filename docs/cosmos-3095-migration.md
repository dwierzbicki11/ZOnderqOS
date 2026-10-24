# Cosmos 3.0.95 migration — deliberately unmerged

This branch is an **integration staging branch**, not a verified bootable release.
It pins `Cosmos.Sdk`, `Cosmos.Kernel` and `Cosmos.Kernel.System` to **3.0.95**
in `ZonderqOS.csproj`. Do **not** merge or deploy to physical disks yet.

## Required follow-up gates

1. Confirm the upstream `CosmosOS/Cosmos` tag `v3.0.95` and package origins.
   Never resolve SDK 3.0.95 against runtime 3.0.94 or legacy 3.0.85.
2. Migrate the *whole* isolated build and packaging chain
   (`tools/build-cosmos-3094-local.sh`, `run-3094.sh`, version compatibility
   guards, dedicated CI, keyboard smoke tests) to an exact-tag 3.0.95 toolchain.
   Keep the 3.0.94 path available for rollback until 3.0.95 is proven.
3. Pass actual x64 and ARM64 C# compilation, verify USB HID/xHCI and keyboard
   input APIs, storage safety guards, QEMU boot and two-boot persistence.
4. Validate managed SMP/OrionGC Stage 23 independently; passing a version guard
   is not proof of SMP behavior.
5. Rebase the isolated E1000E N1 QEMU proof from PR #189 only after the
   3.0.95 build path works. Require real serial MAC/link evidence. Do not
   claim DHCP or IP assignment from PCI enumeration or link state.
6. Keep production `CosmosEnableNetwork=false`; never write Kali EFI
   or mount arbitrary FAT32 partitions. Test bare metal only after CI is green.

The 3.0.94-specific verification workflow and scripts are intentionally
unchanged in this first commit. They are **expected to fail or be inapplicable**
when pointed at the 3.0.95 project: do not paper over those failures by
removing version assertions.

## Current result

- [x] Isolated migration branch created from main.
- [x] SDK / two direct package versions updated in project.
- [ ] Exact 3.0.95 package build and provenance audit.
- [ ] Full build/CI and launcher migration.
- [ ] Real QEMU and hardware results.
- [ ] Merge approved.
