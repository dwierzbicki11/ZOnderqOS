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
service reload
```

`start`, `stop` and `reload` require an authenticated root session. Editing a
configuration requires file permission; use `service reload` to apply it.
`enabled=true` starts a service at boot or on reload. A stopped service can be
started manually even if its configuration says `enabled=false`.

Invalid configuration aborts reload without replacing the previous valid
definitions. Service names use ASCII letters, digits, `_` and `-`, start with a
letter or digit, and are at most 32 characters. Configuration files are limited
to 2048 characters. Intervals range from 5 to 3600 seconds. Unknown and
duplicate settings are rejected.

Custom services currently select one of the supported built-in tasks. The
configuration file cannot launch arbitrary shell commands or programs: that
requires a safe executable loader, process isolation and service credentials.
