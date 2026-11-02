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
of this harness and its linked production sources outside that SDK resolver
scope using an installed SDK and matching target framework. Do not change the
Cosmos package versions to run this test.

These checks validate command orchestration and error propagation, not the
Cosmos FAT driver, persistence on reboot, device removal, arbitrary binary transfers,
or free-space accounting. Cross-filesystem transfers still use the existing text
API. A failed source deletion intentionally leaves both copies and reports failure.
A failed physical copy may leave a partial destination; the command must not
report it as a successful copy or update destination permissions.

Disk-to-RAM transfers now use the linked production `RamTextTransfer.cs` helper.
It reads raw bytes, decodes UTF-8 without BOM detection/removal, then verifies an
exact byte-for-byte encoding round trip before creating any RAM destination.
A non-round-trippable file is rejected and its source remains untouched, including
for `mv`. This is a conservative guard for the existing string-only RAM storage,
not arbitrary binary-file support. It allocates whole-file byte/string buffers;
streaming, durable writes and concurrent source mutation are outside this test.

Twelve additional cases (23 total command checks) cover copy and move of invalid
UTF-8, UTF-16 BOM input, a lone continuation byte, empty data, UTF-8 BOM and
embedded NUL/non-ASCII/supplementary characters. The old commands fail the new
regression with `lossy transfer reported success`. Fault-injected read exceptions
also run through the new raw-byte boundary. No physical disks are accessed.

RAM-to-disk cp/mv now validate UTF-16 before opening a physical destination.
Twenty malformed cases (five payloads, both commands, both RAM roots) assert
failure, exact source preservation and no physical destination. Twenty valid
cases assert exact UTF-8 bytes and source lifetime, including BOM, NUL, Polish
text, supplementary characters and boundary surrogate pairs. This brings the
command suite to 63 checks. The previous command code fails with
`malformed transfer created a physical destination` using real host WriteAllText.
The new validator scans without allocation and applies only when exporting to
a physical path; RAM-to-RAM transfers retain their existing string semantics.
Encoder behavior on Cosmos remains unverified; no binary RAM support is claimed.
