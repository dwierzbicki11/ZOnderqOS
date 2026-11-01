# Storage command behavior smoke test

This console harness compiles the actual `CmdCp.cs` and `CmdMv.cs` sources.
It uses disposable host files for physical paths and injectable boundary doubles
for Disk, virtual filesystems and permissions. It exercises partial-copy failure,
write failure before source deletion, deletion failure in both RAM/disk directions,
read denial, thrown read errors, and successful copy/move paths. Each case starts
with a stale success status deliberately.

On a working .NET 10 SDK environment:

```sh
dotnet run --project tests/StorageCommandsSmoke/StorageCommandsSmoke.csproj
```

The repository's current `global.json` requests `10.0.0`, which is not a valid SDK
feature-band version. Until that shared build configuration is fixed, run a copy
of this harness and its two linked production sources outside that SDK resolver
scope using an installed SDK and matching target framework. Do not change the
Cosmos package versions to run this test.

These checks validate command orchestration and error propagation, not the
Cosmos FAT driver, persistence on reboot, device removal, binary-safe transfers,
or free-space accounting. Cross-filesystem transfers still use the existing text
API. A failed source deletion intentionally leaves both copies and reports failure.
A failed physical copy may leave a partial destination; the command must not
report it as a successful copy or update destination permissions.
