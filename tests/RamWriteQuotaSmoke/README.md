# RAM append quota behavior

This harness compiles the production TmpFs and RunFs classes. It tests rejected
appends at both the per-file and aggregate quotas, preservation of content and
accounting, null append, exact boundary success, overwrite quota release, and
append creating a missing file. All data is ephemeral; no disks are accessed.

Run with a working SDK resolver:

```sh
dotnet run --project tests/RamWriteQuotaSmoke/RamWriteQuotaSmoke.csproj
```

The allocation assertion measures only TryWrite, after creating the input and
initializing the filesystem. A generous 16 KiB ceiling permits small path/error
allocations but catches concatenation of a 64/128 Ki-character file before quota
rejection. It is a host .NET regression check, not a Cosmos GC benchmark.
Previous production code fails with `rejected append allocated content buffer:
262216` on the /tmp per-file limit. Both quota checks must run under the existing
filesystem lock before concatenation; failed writes must not mutate entries.

14 checks cover both filesystems. Existing deterministic and command suites
remain necessary for path validation, copy/move and entry quota regressions.
No Cosmos build/boot or integration PASS is implied by this harness.
