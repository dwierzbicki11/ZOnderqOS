#!/usr/bin/env python3
"""Fail-closed 3.0.94 native rendezvous abort/release regression.

Source contract checks do not claim a fault-injected hardware test.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
PATCH = (ROOT / "patches/cosmos-3094/0008-native-rendezvous.patch").read_text()

def native_span(label: str, end: str) -> str:
    begin = PATCH.find("+" + label)
    finish = PATCH.find("+" + end, begin + 1)
    if begin < 0 or finish < 0 or finish <= begin:
        raise AssertionError(f"missing native label span: {label} ... {end}")
    return PATCH[begin:finish]


class AbortReleaseContract(unittest.TestCase):
    def test_timeout_and_invalid_use_same_cleanup(self):
        body = native_span(".Lsmp8_timeout:", ".Lsmp8_return:")
        self.assertRegex(body, r"\.Lsmp8_timeout:\n\+    mov r10d, -1\n\+    jmp \.Lsmp8_abort")
        self.assertRegex(body, r"\.Lsmp8_invalid:\n\+    mov r10d, -2\n\+\.Lsmp8_abort:")
        self.assertIn("mov eax, r10d", body)
        # Must never jump straight to the return without cleanup.
        self.assertNotIn("jmp .Lsmp8_return", body)

    def test_release_monotonic_and_atomic(self):
        body = native_span(".Lsmp8_abort:", ".Lsmp8_return:")
        self.assertIn("mov rax, qword ptr [rip + _smp8_request_generation]", body)
        self.assertIn("cmp rax, qword ptr [rip + _smp8_release_generation]", body)
        self.assertIn("jbe .Lsmp8_abort_complete", body)
        self.assertIn("xchg qword ptr [rip + _smp8_release_generation], rax", body)
        self.assertLess(body.index("cmp rax"), body.index("xchg qword ptr"))
        self.assertLess(body.index("jbe .Lsmp8_abort_complete"), body.index("xchg qword ptr"))

    def test_ap_wait_is_released_by_bsp_epoch(self):
        body = native_span(".Lsmp8_ap_wait_release:", ".Lsmp8_ap_release:")
        self.assertIn("cmp qword ptr [rip + _smp8_release_generation], rcx", body)
        self.assertIn("jae .Lsmp8_ap_release", body)

    def test_no_managed_gc_claim(self):
        self.assertIn("Does not suspend managed execution or GC.", PATCH)
        self.assertIn("[SMP3094-STAGE8] PASS", PATCH)


if __name__ == "__main__":
    unittest.main(verbosity=2)
