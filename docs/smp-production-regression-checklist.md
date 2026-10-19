# SMP production regression checklist

Use this checklist before merging scheduler/runtime changes and after nightly SMT
Stage 22/23 runs.

## Required CI gates

- [ ] `SMT stage 22 SMP stress and stability` green for 1/2/4/8/16 logical CPUs.
- [ ] `SMT stage 23 production all-CPU scheduling` green for 1/2/4/8/16 logical CPUs.
- [ ] All QEMU jobs publish serial/build/package-source artifacts.
- [ ] No missing required PASS markers in smoke logs.

## Runtime regressions to verify

### Boot and kernel stability

- [ ] x64 ISO boots in QEMU with no panic/triple-fault/deadlock.
- [ ] CPU discovery and logical mapping markers match requested topology.
- [ ] AP online/ready evidence is present for multicore topologies.

### Shell and core CLI

- [ ] Shell starts and accepts commands after Stage 23 runtime path.
- [ ] `lscpu`, `nproc`, `ps`, `htop` report consistent logical CPU counts.
- [ ] No command-path regressions in CI smoke serial output.

### GUI/session path

- [ ] GUI path still initializes on normal boot images.
- [ ] Login/session init markers appear without fallback loops.

### Storage and filesystem

- [ ] Root filesystem mounts and expected core directories exist.
- [ ] No new storage-related exceptions in serial logs.

### Services

- [ ] `heartbeat` service configuration remains persistent across reboot.
- [ ] Service reload/start/stop behavior remains valid after SMP changes.

### Package manager

- [ ] `zpkg` list/install/remove/verify smoke tests remain green.
- [ ] Package isolation (`/opt/zpkg` and `/var/lib/zpkg`) remains unchanged.

## Cosmos 3.0.94 migration gate (separate branch only)

- [ ] Port performed on a dedicated migration branch.
- [ ] Stage 22 + Stage 23 + functional regressions are all green on that branch.
- [ ] Version pin change to 3.0.94 is merged only after full gate completion.
