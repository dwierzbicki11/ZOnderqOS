# File clipboard failure recovery

The harness compiles the production `source/GUI/FileClipboard.cs` and
`source/Storage/PathResolver.cs`. It uses
real disposable host files and fault-injected I/O/permission boundaries, never
physical block devices. Run with a working .NET 10 SDK:

```sh
dotnet run --project tests/FileClipboardSmoke/FileClipboardSmoke.csproj
```

The repository currently requests invalid SDK version `10.0.0` in `global.json`.
As with the other storage smoke suites, compile the linked production source and
Program.cs outside that SDK resolver scope with an available matching runtime.
Do not downgrade Cosmos to run these tests.

Sixteen checks cover permission failure after native move, partial recursive source
deletion, failed file deletion, a move throwing after changing the filesystem,
partial copy, destination-name collision, binary copy, native move, fallback
recursive move and permission failure after completed copy. The first check fails
against the implementation before destination preservation because it deletes the
only surviving copy. Six additional checks exercise dot-dot self-copy rejection,
same-location aliases, home-directory traversal, read-only source aliases,
permission-store exceptions during preflight, and successful normalized copying.
The self-copy regression fails against commit `29cfec8` before any production
changes: an injected copy boundary stops the recursion safely.

On failure, the clipboard retains the selected destination path and reports it in
the error. Destination data is never automatically removed: it may be complete,
partial, the only surviving copy, or belong to another writer. The user must
inspect it before retrying or removing it. A failed cut keeps its clipboard entry;
if the source has disappeared, the normal subsequent existence check clears it.

These are orchestration tests, not Cosmos FAT-driver, power-loss, GUI rendering or
QEMU integration tests. Copy/delete fallback is not transactional; recursive
source deletion can partially succeed, and metadata can fail after data moved.
The tests do not establish concurrency safety or durable flush semantics.

Path normalization is lexical POSIX normalization shared with the shell; it does
not resolve symlinks, filesystem case aliases, or concurrent path replacement.
Preflight exceptions return an error without clearing the clipboard.
