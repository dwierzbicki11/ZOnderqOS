#!/usr/bin/env python3
"""Fail-closed static contract for guest ISR evidence provenance.

This gate cannot prove that IRQs executed. It prevents the isolated kernel
from claiming that they did before real driver counters are wired in.
"""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]


def check(kernel: str, validator: str) -> None:
    if 'isr_executions=unknown' not in kernel:
        raise ValueError('unverified ISR execution disclaimer removed')
    if 'DRIVER_BOUND=PASS' not in kernel:
        raise ValueError('driver binding diagnostic missing')
    if '[NET3095-ISR] phase=' in kernel:
        if not all(token in kernel for token in ('InterruptCount', 'FramesReceived')):
            raise ValueError('ISR UART markers without both driver counters')
        if 'polling=no' not in kernel:
            raise ValueError('ISR UART markers without polling-disabled evidence')
    for required in ('after[0] <= before[0]', 'after[1] <= before[1]',
                     'polling enabled: IRQ RX not proven', 'missing unique guest boot marker'):
        if required not in validator:
            raise ValueError('guest ISR validator weakened: ' + required)


def self_test() -> None:
    kernel = 'DRIVER_BOUND=PASS isr_executions=unknown'
    validator = ' '.join(('after[0] <= before[0]', 'after[1] <= before[1]',
                          'polling enabled: IRQ RX not proven', 'missing unique guest boot marker'))
    check(kernel, validator)
    for changed in (kernel.replace('unknown', '1'),
                    kernel + ' [NET3095-ISR] phase=before',
                    kernel + ' [NET3095-ISR] phase=before InterruptCount FramesReceived'):
        try:
            check(changed, validator)
        except ValueError:
            pass
        else:
            raise AssertionError('accepted fabricated ISR diagnostic')
    try:
        check(kernel, validator.replace('after[0] <= before[0]', ''))
    except ValueError:
        pass
    else:
        raise AssertionError('accepted weakened validator')


if __name__ == '__main__':
    self_test()
    if '--self-test' not in sys.argv:
        check((ROOT / 'source/Core/NetworkN1ProbeKernel.cs').read_text(encoding='utf-8'),
              (ROOT / 'tests/verify_3095_e1000e_guest_isr.py').read_text(encoding='utf-8'))
    print('[NET3095-ISR-PROVENANCE] PASS static contract only; no QEMU proof')
