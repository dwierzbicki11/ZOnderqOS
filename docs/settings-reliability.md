# System settings reliability

ZOnderqOS persists desktop and system preferences in:

```text
/etc/zonderq/settings.conf
```

## Reliability contract

- settings are serialized to `settings.conf.new`
- the last complete file is moved to `settings.conf.bak`
- the staged file is renamed into place
- failed commits attempt rollback to the previous complete file
- temporary ACL entries are removed after commit/rollback
- the file carries a schema number
- persisted values are validated by type/range before being accepted by diagnostics

## Commands

```text
config validate
config repair
```

`config repair` requires authenticated root and rewrites the currently loaded, already range-validated values into the canonical schema.

Recovery mode also provides:

```text
check-settings
repair-settings CONFIRM
reset-settings CONFIRM
```

Validation is read-only. Repair/reset are explicit administrative actions.
