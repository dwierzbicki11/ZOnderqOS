import hashlib
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from prepare_3095_e1000e_irq_only import OLD, NEW, patch

FIXTURE = ('state.HasLine = true; state.OnInterrupt if (!hasLine && !polling) '
           'state.DrainWork = drain; if (!drain.Schedule())\n' + OLD)
STATE = ('internal void OnInterrupt(InterruptContext context)\n'
         '_interruptCount++; context.Schedule(drain); _framesReceived++; public bool IsPolling')

class IsolatedIrqPatchTests(unittest.TestCase):
    def test_only_expected_block_changes(self):
        self.assertEqual(patch(FIXTURE), FIXTURE.replace(OLD, NEW))

    def test_missing_anchor_rejected(self):
        with self.assertRaises(ValueError):
            patch(FIXTURE.replace(OLD, ''))

    def test_duplicate_anchor_rejected(self):
        with self.assertRaises(ValueError):
            patch(FIXTURE + OLD)

    def test_missing_irq_binding_rejected(self):
        with self.assertRaises(ValueError):
            patch(FIXTURE.replace('state.OnInterrupt', 'no-handler'))

    def test_cli_guard_and_copy(self):
        script = Path(__file__).resolve().parents[1] / 'tools' / 'prepare_3095_e1000e_irq_only.py'
        with tempfile.TemporaryDirectory() as tmp:
            d = Path(tmp)
            source = d / 'E1000EDriver.cs'
            state = d / 'E1000EState.cs'
            output = d / 'patched.cs'
            source.write_text(FIXTURE)
            state.write_text(STATE)
            base = [sys.executable, str(script), '--input', str(source),
                    '--output', str(output), '--state', str(state)]
            bad = subprocess.run(base + ['--expected-sha256', '0'*64], capture_output=True)
            self.assertNotEqual(bad.returncode, 0)
            self.assertFalse(output.exists())
            good = subprocess.run(base + ['--expected-sha256', hashlib.sha256(source.read_bytes()).hexdigest()], capture_output=True)
            self.assertEqual(good.returncode, 0, good.stderr.decode())
            self.assertEqual(output.read_text(), patch(FIXTURE))
            self.assertEqual(source.read_text(), FIXTURE)
            inplace = subprocess.run([sys.executable, str(script), '--input', str(source),
                                      '--output', str(source), '--state', str(state),
                                      '--expected-sha256', hashlib.sha256(source.read_bytes()).hexdigest()], capture_output=True)
            self.assertNotEqual(inplace.returncode, 0)
            # The state source is a reviewed upstream input too.
            state_before = state.read_bytes()
            overwrite_state = subprocess.run([sys.executable, str(script), '--input', str(source),
                '--output', str(state), '--state', str(state),
                '--expected-sha256', hashlib.sha256(source.read_bytes()).hexdigest()], capture_output=True)
            self.assertNotEqual(overwrite_state.returncode, 0)
            self.assertEqual(state.read_bytes(), state_before)
            # Never follow an existing output symlink, even when its target
            # is a third file rather than one of the two SDK inputs.
            target = d / 'unrelated.cs'
            target.write_text('must stay unchanged')
            link = d / 'linked-output.cs'
            link.symlink_to(target)
            through_link = subprocess.run([sys.executable, str(script), '--input', str(source),
                '--output', str(link), '--state', str(state),
                '--expected-sha256', hashlib.sha256(source.read_bytes()).hexdigest()], capture_output=True)
            self.assertNotEqual(through_link.returncode, 0)
            self.assertEqual(target.read_text(), 'must stay unchanged')

if __name__ == '__main__':
    unittest.main()
