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
not arbitrary binary-file support. The bounded implementation below replaces whole-file input allocation; durable
writes and concurrent source mutation remain outside this test.

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

Disk-to-RAM cp/mv now pass the destination's production MaxFileChars quota to
ReadLossless. Input reads use a bounded byte buffer (three bytes per UTF-16 code
unit plus one oversize sentinel) and 4 KiB requests, handling short reads until
EOF or the bound without relying on file length. The decoded character count is
also checked before creating the destination. UTF-8 round-trip validation remains.
Twenty quota cases exercise both commands and both targets: 1 MiB oversize input,
ASCII and three-byte characters exactly at and one character above each quota.
The stream boundary forces 137-byte reads and checks bytes consumed and disposal.
Two more cases inject disconnection during reading and assert failure, retained
source, no destination and disposed stream. There are now 85 command checks.
The previous production sources fail with `unbounded physical read`.
This bounds transfer allocation, not read latency or the source's total size on
disk. Available aggregate RAM/entry quotas are still enforced by TryWrite after
reading; no reservation or atomic cross-filesystem transaction is claimed.
