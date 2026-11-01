# Mounted root identity smoke test

Run `dotnet run --project tests/StorageMountSmoke/StorageMountSmoke.csproj`
with a working .NET 10 SDK. The repository currently requests unavailable SDK
`10.0.0` in global.json. An isolated Roslyn compilation with a matching runtime
can run this harness without altering Cosmos SDK/Kernel 3.0.95.

This harness compiles production `StorageMountManager` and `DiskRootBinding`.
Only discovery, VFS and notification APIs are doubles. A test-only partial Disk
sets the same private root binding that Disk.Initialize sets after mounting.
The full Disk initialization and actual Cosmos drivers are not executed.

Coverage: replacing a removed disk with a same-label disk, duplicate labels,
discovery reorder, label change without re-running policy reads, RAM root,
missing/unrelated VFS mounts, explicit VFS partition association, root unmount
protection, invalid indices/unset binding and disabled manual mounting.

Validation on 2026-11-01: 10/10 checks passed with Roslyn 5.11 and .NET 11 RC
outside the repository SDK resolver scope. Against the previous production
StorageMountManager the replacement test fails with `replacement inherited
root/cache`. Existing suites also passed: 38 allocation, 11 command and five
storage UI checks. Policy checks remain 7/8 (the existing version assertion
requires 3.0.94); full kernel build stops at global.json. QEMU is unavailable.

This is a mount identity regression test, not a hardware hotplug or media-removal
integration test. Object identity prevents discovery-list substitution but cannot
detect replacement media behind the same driver object. No disks are written.
