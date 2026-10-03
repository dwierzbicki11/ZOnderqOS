# Log query subsystem

ZOnderqOS uses a shared bounded log reader for shell and GUI diagnostics.

## Sources

```text
system   -> /var/log/system.log
auth     -> /var/log/auth.log
guardian -> /sysmon.log
error    -> /var/error_log.txt (legacy)
```

## Shell usage

```text
viewlog
viewlog system 40
viewlog auth 100 FAILED
viewlog guardian 50 RAM
viewlog sources
```

The reader:
- streams the file once
- keeps only the newest requested matching lines
- caps output at 200 lines in the shell
- supports case-insensitive filtering
- reuses caller-owned buffers
- performs ring-buffer reordering in-place
- never loads the full log into memory

The Settings log GUI uses the same backend.
