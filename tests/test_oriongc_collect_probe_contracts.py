#!/usr/bin/env python3
"""Safety contract for an explicitly opted-in, QEMU-only OrionGC experiment.

These checks cannot certify GC collection stability inside QEMU.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
PROJECT = (ROOT / "ZonderqOS.csproj").read_text()
COMMANDS = (ROOT / "source/Shell/Command.cs").read_text()
PROBE = (ROOT / "source/Shell/Commands/CmdGcProbe.cs").read_text()
GUARDIAN = (ROOT / "source/Core/MemoryGuardian.cs").read_text()
OPTIMIZER = (ROOT / "source/Core/MemoryOptimizer.cs").read_text()
GUI = (ROOT / "source/GUI/GuiManager.cs").read_text()


class OrionGcProbeContracts(unittest.TestCase):
    def test_gc_probe_disabled_by_default(self):
        self.assertIn('<ZonderqGcProbe Condition="\'$(ZonderqGcProbe)\' == \'\'">false</ZonderqGcProbe>', PROJECT)
        self.assertIn("ZONDERQ_EXPERIMENTAL_GC_PROBE", PROJECT)
        self.assertIn("$(ZonderqRamRoot)", PROJECT)
        self.assertIn("$(CosmosArch)", PROJECT)
        self.assertIn("Experimental GC collection is restricted", PROJECT)

    def test_command_is_gated_at_compile_time(self):
        self.assertTrue(PROBE.lstrip().startswith("#if ZONDERQ_EXPERIMENTAL_GC_PROBE"))
        self.assertIn("#if ZONDERQ_EXPERIMENTAL_GC_PROBE\n            Register(new CmdGcProbe());\n#endif", COMMANDS)

    def test_collection_requires_explicit_root_action_once_per_boot(self):
        self.assertIn('args[2] != "--accept-qemu-crash"', PROBE)
        self.assertIn("SecurityContext.CurrentUid != 0", PROBE)
        self.assertIn("Interlocked.CompareExchange(ref collectedOnce, 1, 0)", PROBE)
        self.assertEqual(PROBE.count("GC.Collect();"), 1)

    def test_production_never_manually_collects(self):
        for code in (GUARDIAN, OPTIMIZER, GUI):
            self.assertNotIn("GC.Collect();", code)
            self.assertNotIn("Heap.Collect();", code)
            self.assertNotIn("GarbageCollector.Collect();", code)

    def test_reports_real_pre_post_page_and_gc_counters(self):
        self.assertIn('Trace("before"', PROBE)
        self.assertIn('Trace("after"', PROBE)
        self.assertIn("PageAllocator.FreePageCount", PROBE)
        self.assertIn("CosmosGc.GetCollectionIndex()", PROBE)
        self.assertIn("CosmosGc.GetTotalCommittedBytes()", PROBE)
        self.assertIn('WriteAscii("[GC_PROBE] stage=")', PROBE)


if __name__ == "__main__":
    unittest.main()
