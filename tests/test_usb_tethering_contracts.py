#!/usr/bin/env python3
"""Static contract checks for ZonderqOS USB Tethering (RNDIS + CDC-ECM) driver."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


class TestUsbTetheringContracts(unittest.TestCase):
    def test_driverkit_registration_and_usb_match(self):
        driver = (ROOT / "source/Hardware/UsbTethering/UsbTetheringDriver.cs").read_text(encoding="utf-8")
        self.assertIn("[Driver(Feature = DriverFeature.Network)]", driver)
        self.assertIn("new UsbMatch(interfaceClass: UsbClassCdcData)", driver)
        self.assertIn("binding.PublishNetwork(state)", driver)
        self.assertIn("binding.TryStartThread(", driver)

    def test_rndis_and_cdc_ecm_protocol_support(self):
        proto = (ROOT / "source/Hardware/UsbTethering/UsbNetProtocol.cs").read_text(encoding="utf-8")
        self.assertIn("OidGenCurrentPacketFilter", proto)
        self.assertIn("UsbNetProtocolKind.Rndis", proto)
        self.assertIn("UsbNetProtocolKind.CdcEcm", proto)
        self.assertIn("UsbNetProtocolKind.CdcNcm", proto)
        self.assertIn("WrapPacket(", proto)
        self.assertIn("TryUnwrapNextPacket(", proto)
        self.assertIn("class NcmProtocol", proto)
        self.assertIn("WrapPacket16(", proto)
        self.assertIn("TryUnwrapNextPacket16(", proto)
        self.assertIn("0xE0", proto)
        self.assertIn("0xEF", proto)
        self.assertIn("0x0D", proto)

    def test_network_facade_reports_usb_tethering(self):
        net = (ROOT / "source/Core/Network.cs").read_text(encoding="utf-8")
        self.assertIn("USB Tethering", net)
        self.assertIn("NotifyHotpluggedAdapter(", net)
        self.assertIn("NotifyAdapterDetached(", net)

    def test_tx_lock_does_not_hold_irq_spinlock_across_bulk_io(self):
        driver = (ROOT / "source/Hardware/UsbTethering/UsbTetheringDriver.cs").read_text(encoding="utf-8")
        self.assertIn("TryClaimTx()", driver)
        self.assertIn("ReleaseTx()", driver)
        self.assertIn("finally", driver)
        self.assertIn("usb.BulkOut(", driver)

        # Verify no `using (txLock.Acquire())` block ever spans blocking USB I/O or Sleep
        marker = "using (txLock.Acquire())"
        idx = 0
        blocks_checked = 0
        while True:
            pos = driver.find(marker, idx)
            if pos == -1:
                break
            brace_open = driver.find("{", pos)
            self.assertNotEqual(brace_open, -1)
            depth = 0
            end = brace_open
            for i in range(brace_open, len(driver)):
                if driver[i] == "{":
                    depth += 1
                elif driver[i] == "}":
                    depth -= 1
                    if depth == 0:
                        end = i
                        break
            block = driver[brace_open:end + 1]
            self.assertNotIn("usb.BulkOut", block)
            self.assertNotIn("usb.BulkIn", block)
            self.assertNotIn("usb.ClearHalt", block)
            self.assertNotIn("binding.Sleep", block)
            blocks_checked += 1
            idx = end + 1

        self.assertGreaterEqual(blocks_checked, 2)


if __name__ == "__main__":
    unittest.main()
