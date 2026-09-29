# Boot session health and controlled power

ZOnderqOS records the current boot session in:

```text
/var/lib/zonderq/boot-session.state
```

At boot the marker is changed to `state=running`. A normal GUI or shell reboot/shutdown goes through `SystemPower` and records `state=clean` before invoking the Cosmos power backend.

If the next boot sees a previous `running` marker, the previous session is reported as unclean. This covers hard resets, lost power and fatal paths that never reached a controlled power transition.

## Command

```text
bootstatus
```

It reports:
- whether the marker store is available
- whether a previous session exists
- CLEAN / UNCLEAN state
- previous exit reason
- timestamp

## Deliberate exception

`KernelPanic` and `RecoveryMode` keep using raw Cosmos power operations. A reboot from a failed boot or panic must not rewrite the failed session as a clean normal shutdown.
