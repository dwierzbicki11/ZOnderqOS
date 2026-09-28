# Configured services

ZonderqOS reads up to 64 `*.conf` files from `/etc/zservices` at boot. Each
filename is the service name. For example, `/etc/zservices/diskwatch.conf`:

```ini
type=memory
enabled=true
interval_seconds=30
```

`type=memory` records free page counts, and `type=heartbeat` records a timestamp.
Both run as managed background processes visible in `ps`, with logs at
`/var/log/zservices/<name>.log` (rotated after 64 KiB). The boot process creates
an enabled `heartbeat.conf` example only if that file does not exist.

Commands:

```text
service list
service status diskwatch
service stop diskwatch
service start diskwatch
service restart diskwatch
service enable diskwatch
service disable diskwatch
service reload
```

`start`, `stop` and `reload` require an authenticated root session. Editing a
configuration requires file permission; use `service reload` to apply it.
`enable` and `disable` require root, persist `enabled=` in the service's `.conf`
file and apply it immediately. A backup is kept for recovery if power fails
between the old and new configuration file rename.
`enabled=true` starts a service at boot or on reload. A stopped service can be
started manually even if its configuration says `enabled=false`.
Changing a running service's type or interval and reloading restarts it with
the new definition; an unchanged running service keeps its process.

Invalid configuration aborts reload without replacing the previous valid
definitions. Service names use ASCII letters, digits, `_` and `-`, start with a
letter or digit, and are at most 32 characters. Configuration files are limited
to 2048 characters. Intervals range from 5 to 3600 seconds. Unknown and
duplicate settings are rejected.

Custom services currently select one of the supported built-in tasks. The
configuration file cannot launch arbitrary shell commands or programs: that
requires a safe executable loader, process isolation and service credentials.

## QEMU persistence check

Build the x64 ISO with the patched Cosmos toolchain and boot it with the same
writable FAT disk image attached on two consecutive boots. The normal launcher
uses `zonder_disk.img` when present. After initial root password setup and root
login, check:

1. `service list` shows `heartbeat` running and `ps` shows `svc-heartbeat`.
2. `service disable heartbeat`, then `service status heartbeat` reports
   `enabled=False`; `/etc/zservices/heartbeat.conf` contains `enabled=false`.
3. Reboot without replacing the disk image. `service status heartbeat` should
   still report `enabled=False` and stopped.
4. Run `service enable heartbeat`, `service restart heartbeat`, then check
   `service status heartbeat`, `ps` and `/var/log/zservices/heartbeat.log`.
5. Create `/etc/zservices/custom.conf` with `type=memory`, `enabled=true` and
   `interval_seconds=5`; run `service reload` and check `svc-custom` and its log.

The host integration test exercises these manager transitions with real files
and managed threads. A successful compile or host test alone does not prove
that the QEMU boot mount and FAT writes survive a reboot.
