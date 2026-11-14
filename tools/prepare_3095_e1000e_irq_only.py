#!/usr/bin/env python3
"""Opt-in, fail-closed source patch for a disposable Cosmos v3.0.95 SDK tree.

Never patches an installed SDK in place. A copy of E1000EDriver.cs is emitted
under --output; integrate it only into an isolated SDK build and test in QEMU.
"""
import argparse
import hashlib
from pathlib import Path

OLD = """        bool polling = binding.TrySchedulePeriodic(DrainPeriodMilliseconds, drain);
        state.IsPolling = polling;
"""
NEW = """        // TEST-ONLY IRQ mode: periodic drain is disabled when the IRQ is bound.
        // Without a line, preserve the upstream fallback timer and fail closed
        // if neither receive path can be established. Never use in production.
        bool polling = !hasLine && binding.TrySchedulePeriodic(DrainPeriodMilliseconds, drain);
        state.IsPolling = polling;
"""

def patch(source: str) -> str:
    if source.count(OLD) != 1:
        raise ValueError('expected exactly one stock v3.0.95 polling block')
    required = ('state.HasLine = true;', 'state.OnInterrupt',
                'if (!hasLine && !polling)', 'state.DrainWork = drain;',
                'if (!drain.Schedule())')
    for marker in required:
        if marker not in source:
            raise ValueError('unexpected E1000E source layout: ' + marker)
    return source.replace(OLD, NEW)

def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument('--input', type=Path, required=True)
    ap.add_argument('--output', type=Path, required=True)
    ap.add_argument('--state', type=Path, required=True)
    ap.add_argument('--expected-sha256', required=True,
                    help='64-digit SHA256 of reviewed stock v3.0.95 E1000EDriver.cs')
    args = ap.parse_args()
    if args.input.resolve() == args.output.resolve():
        ap.error('in-place SDK modification forbidden')
    original = args.input.read_bytes()
    if hashlib.sha256(original).hexdigest() != args.expected_sha256.lower():
        ap.error('source SHA256 differs from reviewed stock 3.0.95 input')
    state = args.state.read_text(encoding='utf-8')
    for marker in ('internal void OnInterrupt(InterruptContext context)',
                   '_interruptCount++;', 'context.Schedule(drain);',
                   '_framesReceived++;', 'public bool IsPolling'):
        if marker not in state:
            ap.error('state handler provenance missing: ' + marker)
    changed = patch(original.decode('utf-8'))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(changed, encoding='utf-8')
    print('Prepared test-only IRQ patch:', args.output)
    print('NOT BUILT; NOT QEMU-VERIFIED; NOT FOR PRODUCTION')

if __name__ == '__main__':
    main()
