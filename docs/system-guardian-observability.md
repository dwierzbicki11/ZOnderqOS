# System Guardian observability

`SystemGuardian` is the long-running watchdog process for low-memory and log-maintenance conditions.

It now exposes bounded runtime telemetry:

- guardian cycle count
- last observed free-memory percentage
- warning / critical memory alert counters
- legacy `/sysmon.log` truncation count
- guardian fault count
- last bounded fault message
- last completed check timestamp
- current running state

## Memory thresholds

- warning: 25% free RAM
- critical: 15% free RAM
- warning notification rate limit: 5 minutes
- critical notification rate limit: 1 minute

## Diagnostics

Guardian state is surfaced through:
- `sysinfo`
- `sysreport`

The guardian must never force OrionGC collection. GC remains allocator-driven because manual full collection from the scheduled watchdog path has previously destabilized GUI/input workloads.
