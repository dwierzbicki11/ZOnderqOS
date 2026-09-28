# Persistent kernel panic reports

ZOnderqOS keeps the last best-effort panic report at:

```text
/var/crash/last-panic.txt
```

The panic path writes to a temporary file first, preserves the previous complete report during commit and then renames the new report into place.

## Contents

- panic phase
- exception type and bounded message
- memory page state
- scheduler summary
- storage inventory
- recent in-memory system log entries

## Privacy

The report intentionally excludes:
- user identity
- command history
- MAC/network identifiers
- serial numbers

## Commands

```text
crashinfo show
crashinfo clear
```

Clearing requires authenticated root.

Recovery mode also exposes:

```text
last-crash
```

Persistence is strictly best-effort. A failed VFS/storage write must never replace the normal panic/recovery flow.
