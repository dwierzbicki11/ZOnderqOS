# Failed unlock RAM regression — diagnostic branch (QEMU only)

Observed on physical ASUS: after entering one incorrect lock-screen password,
RAM reported approximately 226.75 MB, up from an earlier 220 MB sample.
The exact per-attempt delta has not been isolated.

The fixed successful Lock->Unlock path queues informational audit events in RAM.
An **incorrect** password invokes:
- `UserManager.ValidateCredentialsChars` (PBKDF2; expected to reuse scratch arrays),
- `AuthenticationGuard.RecordFailure` (bounded static array bookkeeping),
- `SecurityLogger.LogEvent("WARN", ...)` (calls persistent `SystemLogger.Log`
  and then flushes pending auth events plus writes `/var/log/auth.log`).
First WARN can flush several prior Lock/Unlock events. Each FAT32 filesystem
operation may allocate GC-managed cluster buffers; exact contribution is unknown.

## QEMU-only opt-in

Build using regular stock Cosmos 3.0.95 x64, but configure
`-p:ZonderqRamRoot=true -p:ZonderqAuthFailMemoryProbe=true`.
The second property defaults to false. An MSBuild error prevents enabling
it without x64 RAM-root. **Do not use this experimental image on physical
hardware or the main USB pendrive.**

Capture COM1, and compare consecutive lines starting with
`[AUTH_FAIL_MEM]`, which report `used_p`, `p_gc`, `p_rat`,
`p_sys`, `heap_kb`, `comm_kb` for:
- `before_verify`: before checking the password,
- `after_verify_fail`: after checking the incorrect password,
- `before_failure_log`: after updating the limiter,
- `before_system_log` and `after_system_log`: persistence to system log,
- `after_auth_queue_flush`: after pending audit log replay,
- `after_auth_log_write`: after persistence of WARN to auth log,
- `after_failure_log`: return to lock screen.

Make a baseline after boot and correct Lock->Unlock warm-up.
Do **one** intentionally incorrect password and measure each phase delta.
Then unlock correctly, re-lock and repeat with a second wrong attempt
(allowing any rate-limiting delay). Compare behavior after the initial
queue is empty. Never print a password or stored hash in COM1.

A persistent logging requirement remains unchanged; failed login attempts
must still be written to disk. Fix any allocation sources without
removing security audit records. Audit queue overflow and failed-write
behavior separately before merging AGY's lock/unlock performance branch.

Contract tests:
`python3 -m pytest -q tests/test_auth_failed_unlock_ram_contracts.py`

This branch only instruments the problem; it makes no claim to fix it.
