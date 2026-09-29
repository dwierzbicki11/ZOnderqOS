# ZOnderqOS package manager roadmap

The package manager is intentionally split from the future user-process loader.

## Package layout (Stage P1)

A local package is a directory:

```text
my-package/
├── package.zpkg
└── payload/
    └── ...
```

Minimal manifest:

```ini
name=example
version=1.0.0
description=Example data package
```

## Stage P1 - isolated local package store

Status: implemented on this branch.

- `zpkg list`
- `zpkg info <name>`
- `zpkg verify <package-directory>`
- `zpkg install <package-directory>`
- `zpkg remove <name>`
- `zpkg repair <name> <version>` clears an interrupted install for that exact version only when no registry entry exists
- installed payload lives only under `/opt/zpkg/<name>/<version>`
- package registry lives under `/var/lib/zpkg`
- install/remove require authenticated root
- max 4096 total files/directories, 32 directory levels and 256 MiB per package
- manifests are limited to 4096 characters
- concurrent package mutations are serialized
- installs use same-filesystem staging for payload and registry metadata, publishing metadata only after the payload is committed
- stale interrupted staging directories are cleaned on the next install attempt; an orphaned final payload after a reset requires explicit `repair`
- package scripts are not supported
- package payload is never copied into `/bin`, `/lib` or kernel directories

## Stage P2 - integrity metadata

Add per-file hashes and manifest versioning before allowing activation outside the isolated store.

## Stage P3 - activation

Blocked on the executable/process roadmap. Activation may expose package-provided executables or libraries only after the loader, process isolation and permission model define a safe contract.

## Stage P4 - repository/network transport

Blocked on the networking roadmap. Remote indexes, signatures and downloads must not be added before the local package format and integrity verification are stable.

## Non-goals for Stage P1

- no network downloads
- no post-install/pre-remove scripts
- no kernel modules
- no automatic dependency execution
- no package-controlled writes outside the isolated store
