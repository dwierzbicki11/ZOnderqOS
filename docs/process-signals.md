# Process signals in ZonderqOS

This work ports the relevant functionality of the older open PR #119 onto
the current Cosmos Gen3 SMP mainline and addresses the lock hazard in PR #95.

## Supported

- `kill PID`: cooperative SIGTERM, backwards compatible with ProcessManager.Kill.
- `kill -TERM PID` / `kill -15 PID`: cooperative SIGTERM.
- `kill -INT PID` / `kill -2 PID`: cooperative SIGINT.
- `kill -l`: list supported signals and advertise unsupported SIGKILL.
- `killall [-s TERM|INT] exact-process-name`: signal all matching processes.
- `ps`: show last delivered signal number.
- `/proc/PID/status`: adds a `LastSignal` field (0 when no signal was sent).

These signals request cooperative shutdown through the process cancellation
token. SIGKILL is **not supported** because Cosmos has no safe production
mechanism for arbitrary managed-thread termination / Ring 3 process isolation.

## Concurrency and lifecycle

A `CancellationTokenSource.Cancel` callback can synchronously block while
waiting for another CPU to access the process registry. Calling `Cancel`
inside `_registryLock` can therefore deadlock. Signal dispatch now takes
the process reference and records the signal under the lock, then releases
it before `Cancel`. The race with disposal on process exit returns `false`.
An exception from a cancellation callback is logged without crashing the
shell, and the cancellation request still succeeds.

## Tests

`tests/ProcessSignalsSmoke` is a real host-.NET test of the same source files
that the kernel compiles. It checks signal parsing, unsupported SIGKILL,
commands, per-process last signal state, process cleanup and -- importantly --
cancellation callbacks waiting for a **different thread** to acquire the
process registry lock. Reentrant single-thread lock tests would not catch
this deadlock.

The process-signals GitHub Action also compiles x64 and ARM64 and reruns
the existing persistent service lifecycle smoke test. Existing Stage 22 and
Stage 23 CI/QEMU workflows are required before considering integration.
