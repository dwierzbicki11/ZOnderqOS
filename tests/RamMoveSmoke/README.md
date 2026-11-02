# RAM move behavior checks

Runs production `CmdMv`, `TmpFs` and `RunFs` with isolated metadata/path
boundaries. The write boundary delegates to the real RAM filesystem quota
checks; a separate delete or physical move is an unexpected operation.

With the repository's SDK requirement satisfied:

```sh
dotnet run --project tests/RamMoveSmoke/RamMoveSmoke.csproj
```

Sixteen command checks exercise both RAM filesystems: rename with all character
quota consumed, destination collision, rename with all entries consumed, missing
parent, overlong target, missing source, same-path rejection and ordinary rename.
Checks also verify original content, source lifetime, error messages and quota
accounting after success/failure. No host disk writes or FAT driver calls occur.
Cross-filesystem transfers remain covered by `StorageCommandsSmoke`.

On 2026-11-02 all 16 passed when compiled directly with Roslyn and .NET 11 RC
reference assemblies. The previous production command (4aa66c7) failed the first
full-quota rename. Full Cosmos build/boot remains unverified: `global.json`
requests unavailable SDK 10.0.0 and this environment has no QEMU.
