using System;
using ZonderqOS.Hardware;

// Actual C# execution of the same PCI walker and metadata types used by
// ZonderqOS. This is a synthetic config-space test, NOT a physical NIC proof.
var config = new FakePciConfig();
var devices = new PciDiscoveryService(new PciConfigDiscoverySource(config)).DiscoverDevices();
Check(devices.Count == 3, "Expected host bridge and two network functions");
var realtek = devices.Find(d => d.VendorId == 0x10EC && d.DeviceId == 0x8136);
Check(realtek != null, "RTL810xE PCI function missing");
Check(realtek.Id.Address == "00:02.0", "RTL810xE BDF changed");
Check(realtek.RevisionId == 0x06, "RTL810xE silicon revision not forwarded");
Check(realtek.SubsystemVendorId == 0x1043 && realtek.SubsystemDeviceId == 0x200F,
    "RTL810xE subsystem vendor/device not forwarded");
Check(realtek.ClassCode == 0x02 && realtek.Subclass == 0x00, "RTL810xE class changed");
var unusual = devices.Find(d => d.VendorId == 0x1234 && d.DeviceId == 0x5678);
Check(unusual != null && unusual.RevisionId == 0x04, "Non-type-0 network revision lost");
Check(unusual.SubsystemVendorId == 0xFFFF && unusual.SubsystemDeviceId == 0xFFFF,
    "Non-type-0 header must not fabricate subsystem IDs");
var host = devices.Find(d => d.Id.Address == "00:00.0");
Check(host != null && host.RevisionId == 0xFF && host.SubsystemVendorId == 0xFFFF,
    "Non-network PCI function must keep unknown metadata");
Check(config.SubsystemReads == 2, "Only type-0 network function may read subsystem registers");
var legacy = new DeviceDescriptor(new DeviceId("pci", "00:01.0"), 0x8086, 0x100E, 0x02, 0x00, 0x00);
Check(legacy.RevisionId == 0xFF && legacy.SubsystemVendorId == 0xFFFF,
    "Legacy device descriptor construction must stay compatible");


// The isolated E1000E probe must verify the actual QEMU PCI function and
// MMIO/bus-master prerequisites, not merely a published DriverKit name.
// All accesses here are synthetic reads; this is NOT a real DMA/IRQ test.
var e1000 = new FakeE1000EPciConfig();
Check(E1000EPciIdentity.TryReadReady(e1000, out ushort e1000Command, out ulong e1000Bar0) &&
      e1000Command == 0x0006 && e1000Bar0 == 0xFEBF0000UL && e1000.ReadCount == 18,
    "Expected read-only E1000E PCI identity and bus-master/MMIO gate");
var e1000_64 = new FakeE1000EPciConfig { Bar0Low = 0xFEBF0004u, Bar0High = 0x00000001u };
Check(E1000EPciIdentity.TryReadReady(e1000_64, out e1000Command, out e1000Bar0) &&
      e1000Bar0 == 0x00000001FEBF0000UL && e1000_64.ReadCount == 22,
    "E1000E must decode a valid 64-bit memory BAR without writing config space");
e1000.HeaderType = 0x80;
Check(E1000EPciIdentity.TryReadReady(e1000, out e1000Command),
    "Multifunction bit must not invalidate a type-0 E1000E function");
foreach (Action<FakeE1000EPciConfig> corrupt in new Action<FakeE1000EPciConfig>[] {
    x => x.VendorId = 0x10EC,
    x => x.DeviceId = 0x100E,
    x => x.ClassCode = 0x06,
    x => x.Subclass = 0x80,
    x => x.ProgrammingInterface = 0x01,
    x => x.HeaderType = 0x01,
    x => x.Command = 0x0002, // MMIO but no DMA bus master
    x => x.Command = 0x0004, // DMA but no MMIO decode
    x => x.Command = 0x0000,
    x => x.Bar0Low = 0x00000000u, // unassigned BAR0
    x => x.Bar0Low = 0x0000C001u, // I/O BAR
    x => x.Bar0Low = 0xFEBF0002u, // reserved memory BAR type
    x => x.Bar0Low = 0xFEBF0006u, // reserved memory BAR type
    x => x.Bar0Low = 0xFEBF0008u, // prefetchable MMIO32 registers are unsafe
    x => { x.Bar0Low = 0xFEBF000Cu; x.Bar0High = 1; }, // prefetchable MMIO64 registers
    x => x.Bar0Low = 0xFFFFFFFFu, // absent function/all ones
    x => x.Bar0Low = 0xFFFFFFF0u, // unassigned 32-bit MMIO BAR (masked all ones)
    x => { x.Bar0Low = 0xFFFFFFF4u; x.Bar0High = 0; }, // 64-bit low sentinel, upper zero
    x => { x.Bar0Low = 0xFFFFFFF4u; x.Bar0High = 1; }, // 64-bit low sentinel, stale upper
    x => { x.Bar0Low = 0xFFFFFFF4u; x.Bar0High = 0xFFFFFFFFu; },
    x => { x.Bar0Low = 0xFEBF0004u; x.Bar0High = 0x00100000u; }, // exceeds x64 52-bit physical limit
    x => { x.Bar0Low = 0xFEBF0004u; x.Bar0High = 0xFFFFFFFFu; }, // invalid high address bits
    x => x.ThrowOnRead = true
})
{
    var bad = new FakeE1000EPciConfig();
    corrupt(bad);
    Check(!E1000EPciIdentity.TryReadReady(bad, out e1000Command) &&
          e1000Command == 0, "Invalid E1000E PCI configuration must fail closed");
}
Check(!E1000EPciIdentity.TryReadReady(null, out e1000Command) &&
      e1000Command == 0, "Missing PCI accessor must fail closed");
// A PCI snapshot must not mix identity, COMMAND or MMIO BAR values from
// two different configurations. All tests execute production C# reads.
foreach (Action<FakeE1000EPciConfig> change in new Action<FakeE1000EPciConfig>[] {
    x => x.ChangeDeviceOnSecondRead = true,
    x => x.ChangeClassOnSecondRead = true,
    x => x.ChangeCommandOnSecondRead = true,
    x => x.ChangeBarLowOnSecondRead = true,
    x => x.ChangeHeaderOnSecondRead = true,
    x => { x.Bar0Low = 0xFEBF0004u; x.Bar0High = 1;
           x.ChangeBarHighOnSecondRead = true; }
})
{
    var unstable = new FakeE1000EPciConfig();
    change(unstable);
    Check(!E1000EPciIdentity.TryReadReady(unstable, out e1000Command, out e1000Bar0) &&
          e1000Command == 0 && e1000Bar0 == 0,
        "Changed PCI identity/COMMAND/BAR must fail closed without stale outputs");
}
Console.WriteLine("[NET3095-E1000E-PCI] PASS C# stable two-pass PCI snapshot (synthetic; no hardware)");
// The legacy IRQ route is PCI metadata, not an interrupt delivery/ISR
// counter. Reject invalid INTx pins/lines and disabled legacy interrupts.
var irqConfig = new FakeE1000EPciConfig();
Check(E1000EPciIdentity.TryReadLegacyIrqRoute(irqConfig, out byte irqLine, out byte irqPin) &&
      irqLine == 11 && irqPin == 1,
    "Expected read-only QEMU legacy IRQ line/pin");
foreach (Action<FakeE1000EPciConfig> corrupt in new Action<FakeE1000EPciConfig>[] {
    x => x.Command = 0x0406, // PCI COMMAND.INTx Disable
    x => x.IrqLine = 0,
    x => x.IrqLine = 0xFF,
    x => x.IrqPin = 0,
    x => x.IrqPin = 5,
    x => x.VendorId = 0x10EC,
    x => x.Bar0Low = 0xFFFFFFF0u,
    x => x.ThrowOnRead = true
})
{
    var invalid = new FakeE1000EPciConfig();
    corrupt(invalid);
    Check(!E1000EPciIdentity.TryReadLegacyIrqRoute(invalid, out irqLine, out irqPin) &&
          irqLine == 0 && irqPin == 0,
        "Invalid legacy IRQ route must fail closed without stale outputs");
}
Check(!E1000EPciIdentity.TryReadLegacyIrqRoute(null, out irqLine, out irqPin) &&
      irqLine == 0 && irqPin == 0, "Missing PCI accessor must not expose an IRQ route");
// Read-only PCI routing can change between samples. Never publish a stale
// legacy IRQ route after a changed line or newly disabled INTx command.
var irqChanged = new FakeE1000EPciConfig { ChangeIrqOnSecondRead = true };
Check(!E1000EPciIdentity.TryReadLegacyIrqRoute(irqChanged, out irqLine, out irqPin) &&
      irqLine == 0 && irqPin == 0,
    "Changed legacy IRQ line must fail closed without stale outputs");
var irqPinChanged = new FakeE1000EPciConfig { ChangeIrqPinOnSecondRead = true };
Check(!E1000EPciIdentity.TryReadLegacyIrqRoute(irqPinChanged, out irqLine, out irqPin) &&
      irqLine == 0 && irqPin == 0,
    "Changed legacy IRQ pin must fail closed without stale outputs");
var irqDisabled = new FakeE1000EPciConfig { DisableIntxAfterIrqRead = true };
Check(!E1000EPciIdentity.TryReadLegacyIrqRoute(irqDisabled, out irqLine, out irqPin) &&
      irqLine == 0 && irqPin == 0,
    "INTx disabled during PCI sampling must fail closed");
var irqBarChanged = new FakeE1000EPciConfig { ChangeBarLowAfterFirstSnapshot = true };
Check(!E1000EPciIdentity.TryReadLegacyIrqRoute(irqBarChanged, out irqLine, out irqPin) &&
      irqLine == 0 && irqPin == 0,
    "Changed MMIO BAR between valid PCI and IRQ snapshots must fail closed");
var irqBarHighChanged = new FakeE1000EPciConfig {
    Bar0Low = 0xFEBF0004u, Bar0High = 1, ChangeBarHighAfterFirstSnapshot = true
};
Check(!E1000EPciIdentity.TryReadLegacyIrqRoute(irqBarHighChanged, out irqLine, out irqPin) &&
      irqLine == 0 && irqPin == 0,
    "Changed 64-bit BAR high half between PCI and IRQ snapshots must fail closed");
// A stable PCI INTx pin/line must not be reported as an active legacy route
// when MSI or MSI-X is enabled. Exercise the production capability walker.
var irqCaps = new FakeE1000EPciConfig { Status = 0x0010 };
Check(E1000EPciIdentity.TryReadLegacyIrqRoute(irqCaps, out irqLine, out irqPin) &&
      irqLine == 11 && irqPin == 1,
    "Disabled MSI and MSI-X must preserve a valid legacy INTx route");
foreach (Action<FakeE1000EPciConfig> corrupt in new Action<FakeE1000EPciConfig>[] {
    x => x.MsiControl = 0x0001, // MSI enable
    x => x.MsiXControl = 0x8000, // MSI-X enable
    x => x.CapabilityPointer = 0x00, // advertised but missing list
    x => x.CapabilityPointer = 0x42, // unaligned pointer
    x => x.CapabilityPointer = 0x3C, // pointer into PCI header
    x => x.MsiNext = 0x40, // capability cycle
    x => x.MsiNext = 0x44, // unsupported intermediate offset
    x => x.MsiNext = 0x48, // MSI-X header inside MSI address/data payload
    x => x.MsiXNext = 0x40, // cycle after MSI-X
    x => x.MsiId = 0xFF, // malformed capability
    x => x.ChangeStatusOnSecondRead = true,
    x => x.ChangeCapabilityPointerOnSecondRead = true,
    x => x.EnableMsiAfterFirstModeScan = true
})
{
    var invalid = new FakeE1000EPciConfig { Status = 0x0010 };
    corrupt(invalid);
    Check(!E1000EPciIdentity.TryReadLegacyIrqRoute(invalid, out irqLine, out irqPin) &&
          irqLine == 0 && irqPin == 0,
        "Enabled or unstable message-signaled IRQ configuration must fail closed");
}
// Reject a capability chain that reinterprets another capability's payload
// as a header. In particular, the old synthetic 0x40 -> 0x48 MSI/MSI-X
// layout overlapped a 10-byte MSI and was not a valid PCI configuration.
var irqOverlap = new FakeE1000EPciConfig { Status = 0x0010, MsiNext = 0x48 };
Check(!E1000EPciIdentity.TryReadLegacyIrqRoute(irqOverlap, out irqLine, out irqPin) &&
      irqLine == 0 && irqPin == 0,
    "Overlapping MSI address/data and MSI-X header must fail closed");
var irqMaskedOverlap = new FakeE1000EPciConfig { Status = 0x0010, MsiControl = 0x0100 };
Check(!E1000EPciIdentity.TryReadLegacyIrqRoute(irqMaskedOverlap, out irqLine, out irqPin),
    "Masked MSI payload must not overlap the following MSI-X header");
var irqMaskedStandalone = new FakeE1000EPciConfig {
    Status = 0x0010, MsiControl = 0x0100, MsiNext = 0
};
Check(E1000EPciIdentity.TryReadLegacyIrqRoute(irqMaskedStandalone, out irqLine, out irqPin) &&
      irqLine == 11 && irqPin == 1,
    "Complete masked MSI without overlapping next capability must remain valid");
Console.WriteLine("[NET3095-E1000E-IRQ-CAP-OVERLAP] PASS C# overlapping capability ranges rejected (synthetic)");
// The MSI/MSI-X control word alone is not a valid capability: required
// address/data/table registers must also fit inside conventional PCI config.
// Test the production length checks, including 64-bit and mask-capable MSI.
var irqTail = new FakeE1000EPciConfig {
    Status = 0x0010, CapabilityPointer = 0xF8, TailCapabilityId = 0x05
};
Check(!E1000EPciIdentity.TryReadLegacyIrqRoute(irqTail, out irqLine, out irqPin) &&
      irqLine == 0 && irqPin == 0,
    "MSI header at F8 with truncated data registers must fail closed");
irqTail.TailCapabilityId = 0x11;
Check(!E1000EPciIdentity.TryReadLegacyIrqRoute(irqTail, out irqLine, out irqPin) &&
      irqLine == 0 && irqPin == 0,
    "MSI-X header at F8 with truncated table/PBA must fail closed");
irqTail.CapabilityPointer = 0xF4;
irqTail.TailCapabilityId = 0x05;
Check(E1000EPciIdentity.TryReadLegacyIrqRoute(irqTail, out irqLine, out irqPin) &&
      irqLine == 11 && irqPin == 1,
    "Minimum 10-byte 32-bit MSI at F4 must remain valid when disabled");
irqTail.TailControl = 0x0080;
Check(!E1000EPciIdentity.TryReadLegacyIrqRoute(irqTail, out irqLine, out irqPin) &&
      irqLine == 0 && irqPin == 0,
    "64-bit MSI at F4 must reject truncated upper address/data");
irqTail.TailControl = 0x0100;
Check(!E1000EPciIdentity.TryReadLegacyIrqRoute(irqTail, out irqLine, out irqPin) &&
      irqLine == 0 && irqPin == 0,
    "Masked 32-bit MSI at F4 must reject truncated mask/pending words");
irqTail.TailControl = 0;
irqTail.TailCapabilityId = 0x11;
Check(E1000EPciIdentity.TryReadLegacyIrqRoute(irqTail, out irqLine, out irqPin) &&
      irqLine == 11 && irqPin == 1,
    "Complete 12-byte disabled MSI-X at F4 must remain valid");
irqTail.TailCapabilityId = 0x05;
irqTail.CapabilityPointer = 0xEC;
irqTail.TailControl = 0x0100;
Check(E1000EPciIdentity.TryReadLegacyIrqRoute(irqTail, out irqLine, out irqPin) &&
      irqLine == 11 && irqPin == 1,
    "Complete 20-byte masked 32-bit MSI at EC must remain valid");
irqTail.CapabilityPointer = 0xF0;
Check(!E1000EPciIdentity.TryReadLegacyIrqRoute(irqTail, out irqLine, out irqPin) &&
      irqLine == 0 && irqPin == 0,
    "20-byte masked 32-bit MSI at F0 must reject overflow");
irqTail.TailControl = 0x0080;
Check(E1000EPciIdentity.TryReadLegacyIrqRoute(irqTail, out irqLine, out irqPin) &&
      irqLine == 11 && irqPin == 1,
    "Complete 14-byte unmasked 64-bit MSI at F0 must remain valid");
irqTail.CapabilityPointer = 0xE8;
irqTail.TailControl = 0x0180;
Check(E1000EPciIdentity.TryReadLegacyIrqRoute(irqTail, out irqLine, out irqPin) &&
      irqLine == 11 && irqPin == 1,
    "Complete 24-byte 64-bit masked MSI at E8 must remain valid");
irqTail.CapabilityPointer = 0xEC;
Check(!E1000EPciIdentity.TryReadLegacyIrqRoute(irqTail, out irqLine, out irqPin) &&
      irqLine == 0 && irqPin == 0,
    "24-byte 64-bit masked MSI at EC must reject overflow");
Console.WriteLine("[NET3095-E1000E-IRQ-CAP-LENGTH] PASS C# bounded MSI/MSI-X layouts (synthetic)");

// An E1000E in D3hot cannot receive legacy interrupts or perform DMA,
// even if COMMAND, BAR0 and the INTx line appear unchanged. Check PMCSR
// through the same read-only PCI capability walker used by the guest.
var irqPm = new FakeE1000EPciConfig { Status = 0x0010, MsiXNext = 0x60 };
Check(E1000EPciIdentity.TryReadLegacyIrqRoute(irqPm, out irqLine, out irqPin) &&
      irqLine == 11 && irqPin == 1,
    "D0 PMCSR must preserve the valid legacy IRQ route");
foreach (Action<FakeE1000EPciConfig> corrupt in new Action<FakeE1000EPciConfig>[] {
    x => x.PmControlStatus = 0x0001, // D1
    x => x.PmControlStatus = 0x0002, // D2
    x => x.PmControlStatus = 0x0003, // D3hot
    x => x.PmId = 0xFF, // malformed PM capability
    x => x.PmNext = 0x60, // self-loop
    x => x.PmNext = 0x68, // duplicate PM capability
    x => x.ChangePmStateOnSecondRead = true, // D0 -> D3hot
    x => x.MsiXNext = 0xFC // PMCSR would wrap past PCI config space
})
{
    var invalid = new FakeE1000EPciConfig { Status = 0x0010, MsiXNext = 0x60 };
    corrupt(invalid);
    Check(!E1000EPciIdentity.TryReadLegacyIrqRoute(invalid, out irqLine, out irqPin) &&
          irqLine == 0 && irqPin == 0,
        "Invalid, non-D0 or unstable PMCSR must fail closed without stale IRQ outputs");
}
var irqPmPme = new FakeE1000EPciConfig {
    Status = 0x0010, MsiXNext = 0x50,
    PmControlStatus = 0x8100, ChangePmPmeOnSecondRead = true
};
Check(E1000EPciIdentity.TryReadLegacyIrqRoute(irqPmPme, out irqLine, out irqPin) &&
      irqLine == 11 && irqPin == 1,
    "PME enable/status changes with power state D0 must not reject INTx");
Console.WriteLine("[NET3095-E1000E-POWER] PASS C# read-only D0 PMCSR gate (synthetic; no DMA/ISR proof)");
Console.WriteLine("[NET3095-E1000E-IRQ-MODE] PASS C# bounded MSI/MSI-X/INTx metadata gate (synthetic; ISR unproven)");
Console.WriteLine("[NET3095-E1000E-IRQ] PASS C# legacy PCI IRQ routing metadata (synthetic; ISR unproven)");

Console.WriteLine("[NET3095-E1000E-PCI] PASS C# read-only PCI identity/MMIO/DMA prerequisites (synthetic; no hardware)");

// Synthetic BAR register fixtures: these execute the production C# decoder,
// not a NIC driver and not real laptop hardware.
var barConfig = new FakeBarConfig();
barConfig.SetBar(0, 0xFEBF0000u);   // non-prefetchable MMIO32
barConfig.SetBar(1, 0x0000C001u);   // I/O at C000
barConfig.SetBar(2, 0x0000000Cu);   // prefetchable 64-bit BAR
barConfig.SetBar(3, 0x00000001u);   // high half, base 0x100000000
Check(PciBarInventory.TryReadRealtek810xE(barConfig, 0, 2, 0, out var bars),
    "Expected type-0 RTL810xE BAR inventory");
Check(bars.Count == 3, "BAR pair must count as one resource");
Check(bars[0].Index == 0 && bars[0].Kind == PciBarKind.Memory32 &&
      bars[0].Address == 0xFEBF0000UL && !bars[0].Prefetchable,
    "MMIO32 BAR decoding failed");
Check(bars[1].Index == 1 && bars[1].Kind == PciBarKind.Io &&
      bars[1].Address == 0xC000UL, "I/O BAR masking failed");
Check(bars[2].Index == 2 && bars[2].Kind == PciBarKind.Memory64 &&
      bars[2].Address == 0x100000000UL && bars[2].Prefetchable,
    "64-bit BAR high half or prefetch bit decoding failed");
Check(barConfig.BarReads == 12, "BAR inventory must read six dwords only");
Check(PciBarInventory.TryReadRealtek810xEState(barConfig, 0, 2, 0, out var state),
    "Expected read-only Realtek PCI state");
Check(state.Command == 0x0007 && state.Status == 0x0010 &&
      state.IoEnabled && state.MemoryEnabled && state.BusMasterEnabled &&
      !state.LegacyInterruptDisabled && state.IrqLine == 11 && state.IrqPin == 1,
    "PCI command/status/IRQ decode failed");
barConfig.SetBar(0, 0);
barConfig.SetBar(1, 0xFFFFFFFFu);
barConfig.SetBar(2, 0);
barConfig.SetBar(3, 0);
Check(PciBarInventory.TryReadRealtek810xE(barConfig, 0, 2, 0, out bars) &&
      bars.Count == 0, "Unassigned BARs must not be reported as usable");
barConfig.SetBar(5, 0x00000004u);
Check(!PciBarInventory.TryReadRealtek810xE(barConfig, 0, 2, 0, out bars) &&
      bars.Count == 0, "Truncated 64-bit BAR must fail closed");
barConfig.SetBar(5, 0);
barConfig.SetBar(0, 0x00000002u); // reserved memory BAR type 01
Check(!PciBarInventory.TryReadRealtek810xE(barConfig, 0, 2, 0, out bars) &&
      bars.Count == 0, "Reserved memory BAR encoding must fail closed");
barConfig.SetBar(0, 0);

// Conventional PCI capabilities: read-only PM -> MSI -> MSI-X linked list.
barConfig.SetCapability(0x40, 0x01, 0x50);
barConfig.SetCapability(0x50, 0x05, 0x60);
barConfig.SetCapability(0x60, 0x11, 0);
Check(PciBarInventory.TryReadRealtek810xECapabilities(barConfig, 0, 2, 0, out var caps),
    "Expected read-only RTL810xE capability walk");
Check(caps.Count == 3 && caps[0].Id == 0x01 && caps[0].Offset == 0x40 &&
      caps[1].Id == 0x05 && caps[1].Offset == 0x50 &&
      caps[2].Id == 0x11 && caps[2].Offset == 0x60,
    "PM/MSI/MSI-X capability list decoding failed");
barConfig.SetCapability(0x60, 0x11, 0x40); // cycle
Check(!PciBarInventory.TryReadRealtek810xECapabilities(barConfig, 0, 2, 0, out caps) &&
      caps.Count == 0, "Cyclic capability list must fail closed without partial data");
barConfig.SetCapability(0x60, 0x11, 0);
barConfig.CapabilityPointer = 0x30;
Check(!PciBarInventory.TryReadRealtek810xECapabilities(barConfig, 0, 2, 0, out caps) &&
      caps.Count == 0, "Out-of-range capability pointer must fail closed");
barConfig.CapabilityPointer = 0x41; // malformed unaligned head
Check(!PciBarInventory.TryReadRealtek810xECapabilities(barConfig, 0, 2, 0, out caps) &&
      caps.Count == 0, "Unaligned capability head must fail closed");
barConfig.CapabilityPointer = 0x40;
barConfig.SetCapability(0x50, 0x05, 0x61); // malformed unaligned next
Check(!PciBarInventory.TryReadRealtek810xECapabilities(barConfig, 0, 2, 0, out caps) &&
      caps.Count == 0, "Unaligned capability next pointer must fail closed");
barConfig.SetCapability(0x50, 0x05, 0x60);
barConfig.CapabilityPointer = 0x40;
barConfig.SetCapability(0x50, 0xFF, 0);
Check(!PciBarInventory.TryReadRealtek810xECapabilities(barConfig, 0, 2, 0, out caps) &&
      caps.Count == 0, "Invalid capability ID must fail closed");
barConfig.SetCapability(0x50, 0x05, 0x60);
barConfig.CapabilityListPresent = false;
int capReadsBefore = barConfig.CapabilityReads;
Check(PciBarInventory.TryReadRealtek810xECapabilities(barConfig, 0, 2, 0, out caps) &&
      caps.Count == 0 && barConfig.CapabilityReads == capReadsBefore,
    "Status without capability-list flag must not walk pointers");
barConfig.CapabilityListPresent = true;
// Read-only MSI and MSI-X controls. The bit being set is NOT an IRQ proof.
barConfig.SetCapability(0x50, 0x05, 0x60);
barConfig.SetMsiControl(0x0081); // MSI enabled + 64-bit capable
barConfig.SetMsixControl(0xC003); // enabled, function masked, 4 vectors
barConfig.SetMsixTable(0x00002002u, 0x00003004u); // BAR2 / BAR4
Check(PciBarInventory.TryReadRealtek810xEInterruptCapabilities(barConfig, 0, 2, 0,
      out var irqInfo), "Expected read-only interrupt capability inventory");
Check(irqInfo.MsiPresent && irqInfo.MsiEnabled && irqInfo.Msi64Bit &&
      irqInfo.MsiXPresent && irqInfo.MsiXEnabled && irqInfo.MsiXFunctionMasked &&
      irqInfo.MsiXVectors == 4 && irqInfo.TableBir == 2 &&
      irqInfo.TableOffset == 0x2000 && irqInfo.PbaBir == 4 &&
      irqInfo.PbaOffset == 0x3000,
    "MSI/MSI-X control, vector count or table/PBA BAR decode failed");
// MSI-X table and PBA must point to assigned MEMORY BARs. A 64-bit
// BAR's high half is never an independently addressable BAR.
// Earlier negative BAR fixtures cleared BAR2/3: explicitly restore them.
barConfig.SetBar(2, 0x0000000Cu);
barConfig.SetBar(3, 0x00000001u);
Check(!PciBarInventory.TryResolveRealtek810xEMsiXMemoryBars(barConfig, 0, 2, 0,
      out var msixBars), "Unassigned PBA BAR must fail closed");
barConfig.SetBar(4, 0xFEBE0000u);
Check(PciBarInventory.TryResolveRealtek810xEMsiXMemoryBars(barConfig, 0, 2, 0,
      out msixBars) && msixBars.TableBir == 2 &&
      msixBars.TableAddress == 0x100002000UL &&
      msixBars.PbaBir == 4 && msixBars.PbaAddress == 0xFEBE3000UL &&
      msixBars.TableBytes == 64u && msixBars.PbaBytes == 8u,
    "MSI-X memory BAR address resolution failed");
// Reject overlapping table/PBA windows, including two offsets in one BAR.
barConfig.SetMsixTable(0x00002002u, 0x00002022u);
Check(!PciBarInventory.TryResolveRealtek810xEMsiXMemoryBars(barConfig, 0, 2, 0,
      out msixBars), "Overlapping MSI-X table/PBA windows must fail closed");
barConfig.SetMsixTable(0x00002002u, 0x00002042u);
Check(PciBarInventory.TryResolveRealtek810xEMsiXMemoryBars(barConfig, 0, 2, 0,
      out msixBars) && msixBars.PbaAddress == 0x100002040UL,
    "Adjacent non-overlapping MSI-X windows in one BAR should resolve");
barConfig.SetMsixTable(0x00002002u, 0x00003004u);
barConfig.SetMsixControl(0xC7FF); // 2048 vectors, maximum table/PBA sizes
Check(PciBarInventory.TryResolveRealtek810xEMsiXMemoryBars(barConfig, 0, 2, 0,
      out msixBars) && msixBars.TableBytes == 32768u && msixBars.PbaBytes == 256u,
    "Maximum MSI-X vector count must produce bounded table/PBA spans");
barConfig.SetMsixControl(0xC003);
barConfig.SetMsixTable(0x00002003u, 0x00003004u); // BAR3 is high half of BAR2
Check(!PciBarInventory.TryResolveRealtek810xEMsiXMemoryBars(barConfig, 0, 2, 0,
      out msixBars), "64-bit BAR high half must never resolve MSI-X table");
barConfig.SetMsixTable(0x00002002u, 0x00003004u);
barConfig.SetBar(4, 0x0000C001u); // I/O BAR is invalid for MSI-X PBA
Check(!PciBarInventory.TryResolveRealtek810xEMsiXMemoryBars(barConfig, 0, 2, 0,
      out msixBars), "MSI-X PBA must not resolve through I/O BAR");
barConfig.SetBar(4, 0xFEBE0000u);
barConfig.SetBar(2, 0xFFFFF00Cu);
barConfig.SetBar(3, 0xFFFFFFFFu);
Check(!PciBarInventory.TryResolveRealtek810xEMsiXMemoryBars(barConfig, 0, 2, 0,
      out msixBars), "MSI-X table address overflow must fail closed");
// A range ending exactly at ulong.MaxValue is valid; one byte beyond is not.
barConfig.SetMsixTable(0x00000FC2u, 0x00003004u);
Check(PciBarInventory.TryResolveRealtek810xEMsiXMemoryBars(barConfig, 0, 2, 0,
      out msixBars), "MSI-X table ending at ulong.MaxValue is representable");
barConfig.SetMsixTable(0x00000FCAu, 0x00003004u);
Check(!PciBarInventory.TryResolveRealtek810xEMsiXMemoryBars(barConfig, 0, 2, 0,
      out msixBars), "MSI-X table span overflow must fail closed");
barConfig.SetMsixControl(0xC040); // 65 vectors -> two PBA words
barConfig.SetMsixTable(0x00002004u, 0x00000FFAu); // PBA on near-max BAR2
Check(!PciBarInventory.TryResolveRealtek810xEMsiXMemoryBars(barConfig, 0, 2, 0,
      out msixBars), "MSI-X PBA span overflow must fail closed");
barConfig.SetMsixControl(0xC003);
barConfig.SetBar(2, 0x0000000Cu);
barConfig.SetBar(3, 0x00000001u);
barConfig.SetBar(4, 0);
barConfig.SetMsixTable(0x00002007u, 0x00003004u); // invalid BAR7
Check(!PciBarInventory.TryReadRealtek810xEInterruptCapabilities(barConfig, 0, 2, 0,
      out irqInfo), "MSI-X BAR indicator 6/7 must fail closed");
barConfig.SetMsixTable(0x00002002u, 0x00003004u);
// PMCSR D3hot + PME enabled/status. Do not transition to D0.
barConfig.SetPmcsr(0x8103);
Check(PciBarInventory.TryReadRealtek810xEPowerState(barConfig, 0, 2, 0,
      out var powerInfo) && powerInfo.PmPresent && powerInfo.State == 3 &&
      powerInfo.PmeEnabled && powerInfo.PmeStatus,
    "Read-only PMCSR power-state decode failed");
barConfig.CapabilityPointer = 0x50; // no PM capability
Check(PciBarInventory.TryReadRealtek810xEPowerState(barConfig, 0, 2, 0,
      out powerInfo) && !powerInfo.PmPresent,
    "Absent PM capability must not fabricate a power state");
barConfig.CapabilityPointer = 0x40;
barConfig.HeaderType = 1;
Check(!PciBarInventory.TryReadRealtek810xEState(barConfig, 0, 2, 0, out state),
    "Non-type-0 PCI state must fail closed");
Check(!PciBarInventory.TryReadRealtek810xECapabilities(barConfig, 0, 2, 0, out caps),
    "Non-type-0 PCI capability list must fail closed");
int previousReads = barConfig.BarReads;
Check(!PciBarInventory.TryReadRealtek810xE(barConfig, 0, 2, 0, out bars) &&
      barConfig.BarReads == previousReads, "Non-type-0 headers must not read BARs");
barConfig.HeaderType = 0;
barConfig.VendorId = 0x1234;
Check(!PciBarInventory.TryReadRealtek810xEState(barConfig, 0, 2, 0, out state),
    "Non-Realtek PCI state must fail closed");
Check(!PciBarInventory.TryReadRealtek810xE(barConfig, 0, 2, 0, out bars) &&
      barConfig.BarReads == previousReads, "Non-Realtek function must not read BARs");
barConfig.VendorId = 0x10EC;
Check(!PciBarInventory.TryReadRealtek810xE(barConfig, 0, 32, 0, out bars),
    "Invalid PCI device number must fail closed");
// A corrupt PM capability pointer must never be dereferenced past 0xFF.
Check(Realtek810xERegisterDecoder.IsSafePowerManagementCapabilityOffset(0x40) &&
      Realtek810xERegisterDecoder.IsSafePowerManagementCapabilityOffset(0xF8),
    "Aligned conventional PCI PM capability bounds must be accepted");
foreach (int invalid in new[] { 0, 0x3C, 0x41, 0xF9, 0xFC, 0xFF })
    Check(!Realtek810xERegisterDecoder.IsSafePowerManagementCapabilityOffset((byte)invalid),
        "Malformed PCI PM capability pointer must fail closed: " + invalid);
for (int offset = 0; offset < 256; offset++)
{
    bool expected = offset >= 0x40 && offset <= 0xF8 && (offset & 3) == 0;
    Check(Realtek810xERegisterDecoder.IsSafePowerManagementCapabilityOffset((byte)offset) == expected,
        "PM capability pointer exhaustive range/alignment guard failed: " + offset);
}
// Exhaustive 256x256 PM capability pointer transitions; no hardware access.
Check(Realtek810xERegisterDecoder.IsStablePowerManagementCapability(0, 0),
    "Consistently absent PM capability must remain valid");
foreach (int offset in new[] { 0x40, 0x80, 0xF8 })
{
    Check(Realtek810xERegisterDecoder.IsStablePowerManagementCapability((byte)offset, (byte)offset),
        "Stable aligned PM capability pointer must be accepted");
    Check(!Realtek810xERegisterDecoder.IsStablePowerManagementCapability((byte)offset, 0),
        "Disappearing PM capability must fail closed");
    Check(!Realtek810xERegisterDecoder.IsStablePowerManagementCapability(0, (byte)offset),
        "New PM capability must fail closed");
}
for (int before = 0; before < 256; before++)
{
    for (int after = 0; after < 256; after++)
    {
        bool expected = before == after &&
            (before == 0 || (before >= 0x40 && before <= 0xF8 && (before & 3) == 0));
        Check(Realtek810xERegisterDecoder.IsStablePowerManagementCapability((byte)before, (byte)after) == expected,
            "PM capability stability guard mismatch: " + before + "/" + after);
    }
}
Console.WriteLine("[NET3095-RTL810XE-PM-STABILITY] PASS C# exhaustive PM capability transition guard; no hardware access");
Console.WriteLine("[NET3095-RTL810XE-PM-POINTER] PASS C# exhaustive conventional PCI PM capability pointer guard; no hardware access");

// Fail closed when BAR2 is not MMIO or the register BAR pair changes
// while a read-only snapshot is being sampled. No real PCI access here.
const ulong bar2Mmio32 = 0x00000000FEBF0000UL;
const ulong bar2Mmio64 = 0x00000001FEBF0004UL;
Check(Realtek810xERegisterDecoder.IsStableMmioBar2Pair(bar2Mmio32, bar2Mmio32),
    "Stable 32-bit MMIO BAR2 must be accepted");
Check(Realtek810xERegisterDecoder.IsStableMmioBar2Pair(bar2Mmio64, bar2Mmio64),
    "Stable 64-bit MMIO BAR2/BAR3 must be accepted");
Check(Realtek810xERegisterDecoder.IsStableMmioBar2Pair(
      0x00000000FEBF0008UL, 0x00000000FEBF0008UL),
    "Prefetchable MMIO BAR2 is still a memory BAR");
Check(!Realtek810xERegisterDecoder.IsStableMmioBar2Pair(
      bar2Mmio32, 0x00000000FEBE0000UL),
    "Changed BAR2 address must invalidate the register sample");
Check(!Realtek810xERegisterDecoder.IsStableMmioBar2Pair(
      bar2Mmio64, 0x00000002FEBF0004UL),
    "Changed 64-bit BAR3 upper address must invalidate the register sample");
Check(!Realtek810xERegisterDecoder.IsStableMmioBar2Pair(
      bar2Mmio32, 0x00000001FEBF0000UL),
    "Changed BAR3 must fail closed even with a 32-bit BAR2");
foreach (ulong badBar in new[] {
    0x00000000FEBF0001UL, // I/O BAR
    0x00000000FEBF0002UL, // reserved memory type 01
    0x00000000FEBF0006UL  // reserved memory type 11
})
    Check(!Realtek810xERegisterDecoder.IsStableMmioBar2Pair(badBar, badBar),
        "Unsupported BAR2 encoding must be rejected: " + badBar.ToString("X16"));
// A BAR with only attribute bits set is not a valid register window.
foreach (ulong unassigned in new[] {
    0UL, 0x8UL, 0x4UL, 0x0000000100000000UL
})
    Check(!Realtek810xERegisterDecoder.IsStableMmioBar2Pair(unassigned, unassigned),
        "Zero-address BAR2 must fail closed even if metadata claims it is assigned");
Check(Realtek810xERegisterDecoder.IsStableMmioBar2Pair(
      0x0000000100000004UL, 0x0000000100000004UL),
    "A valid 64-bit BAR2 with its base entirely in BAR3 must be accepted");
// Verify the physical BAR used by MapRegisters matches PCI config space.
Check(Realtek810xERegisterDecoder.IsBar2MetadataConsistent(
      bar2Mmio32, 0xFEBF0000UL, 0x1000UL),
    "Matching 32-bit BAR2 resource metadata must be accepted");
Check(Realtek810xERegisterDecoder.IsBar2MetadataConsistent(
      bar2Mmio64, 0x00000001FEBF0000UL, 0x1000UL),
    "Matching 64-bit BAR2 resource metadata must be accepted");
Check(Realtek810xERegisterDecoder.IsBar2MetadataConsistent(
      0x0000000100000004UL, 0x0000000100000000UL, 0x1000UL),
    "64-bit BAR2 whose base is entirely in BAR3 must be accepted");
foreach (ulong wrongBase in new ulong[] { 0UL, 0xFEBE0000UL, 0x00000001FEBF0000UL })
    Check(!Realtek810xERegisterDecoder.IsBar2MetadataConsistent(
          bar2Mmio32, wrongBase, 0x1000UL),
        "Mismatched DriverKit BAR2 base must be rejected");
Check(!Realtek810xERegisterDecoder.IsBar2MetadataConsistent(
      bar2Mmio64, 0xFEBF0000UL, 0x1000UL),
    "Truncated 64-bit BAR2 base must be rejected");
Check(!Realtek810xERegisterDecoder.IsBar2MetadataConsistent(
      bar2Mmio32, 0xFEBF0000UL, 0x6CUL),
    "Too-short mapped register aperture must be rejected");
// RTL810xE BAR2 holds live device registers: a prefetchable BAR would be
// unsafe for a read-only MMIO probe even when DriverKit metadata agrees.
foreach (ulong prefetchable in new ulong[] {
    0x00000000FEBF0008UL, 0x00000001FEBF000CUL
})
    Check(!Realtek810xERegisterDecoder.IsBar2MetadataConsistent(
          prefetchable, prefetchable & ~0xFUL, 0x1000UL),
        "Prefetchable RTL810xE register BAR must fail closed");
Check(!Realtek810xERegisterDecoder.IsBar2MetadataConsistent(
      0xFFFFFFFFFFFFF004UL, 0xFFFFFFFFFFFFF000UL, 0x2000UL),
    "Wrapped mapped register aperture must be rejected");
foreach (ulong invalidRaw in new ulong[] { 0UL, 0xFEBF0001UL, 0xFEBF0002UL, 0xFEBF0006UL })
    Check(!Realtek810xERegisterDecoder.IsBar2MetadataConsistent(
          invalidRaw, 0xFEBF0000UL, 0x1000UL),
        "Invalid raw BAR2 type or address must be rejected");
Console.WriteLine("[NET3095-RTL810XE-BAR-METADATA] PASS C# physical BAR/resource consistency guards; no hardware access");
Console.WriteLine("[NET3095-RTL810XE-BAR-STABILITY] PASS C# BAR2/BAR3 MMIO type and relocation guards; no hardware MMIO proof");

// The probe must not enable bus mastering, disable MMIO or otherwise
// modify the command register while mapping and reading registers.
foreach (ushort stableCommand in new ushort[] { 0x0002, 0x0003, 0x0006, 0x0402 })
    Check(Realtek810xERegisterDecoder.IsStablePciCommand(stableCommand, stableCommand),
        "Unchanged memory-enabled PCI command should be accepted");
foreach (ushort disabled in new ushort[] { 0x0000, 0x0001, 0x0004 })
    Check(!Realtek810xERegisterDecoder.IsStablePciCommand(disabled, disabled),
        "Memory-disabled PCI command must be rejected");
Check(!Realtek810xERegisterDecoder.IsStablePciCommand(0x0002, 0x0006),
    "Unexpected bus-master enable must invalidate the sample");
Check(!Realtek810xERegisterDecoder.IsStablePciCommand(0x0006, 0x0002),
    "Unexpected bus-master disable must invalidate the sample");
Check(!Realtek810xERegisterDecoder.IsStablePciCommand(0x0002, 0x0000),
    "MMIO decoder disabled during sampling must invalidate the sample");
// Full PMCSR must remain unchanged across mapping AND both MMIO read passes.
// A D0-only check would miss unexpected PME/status changes during probing.
for (int raw = 0; raw <= ushort.MaxValue; raw++)
{
    ushort pmcsr = (ushort)raw;
    bool validD0 = (pmcsr & 0x0003) == 0;
    Check(Realtek810xERegisterDecoder.IsStablePowerManagementControl(pmcsr, pmcsr) == validD0,
        "PMCSR stable/D0 exhaustive check failed");
    Check(!Realtek810xERegisterDecoder.IsStablePowerManagementControl(pmcsr, (ushort)(pmcsr ^ 0x0100)),
        "PMCSR PME/status transition must fail closed");
}
Check(!Realtek810xERegisterDecoder.IsStablePowerManagementControl(0x0000, 0x0001),
    "PMCSR D0 to D1 transition must fail closed");
Check(!Realtek810xERegisterDecoder.IsStablePowerManagementControl(0x0000, 0x8000),
    "PMCSR PME status transition must fail closed");
Console.WriteLine("[NET3095-RTL810XE-PMCSR-STABILITY] PASS C# exhaustive PMCSR transition guard; no physical PCI access");

Console.WriteLine("[NET3095-RTL810XE-COMMAND-STABILITY] PASS C# PCI command snapshot guard; no hardware access");

// Exact PCI header guard executes as pure C#; it never accesses a real NIC.
Check(Realtek810xERegisterDecoder.IsExactAsusRtl810xE(
      0x10EC, 0x8136, 0x0006, 0x0200, 0x0000, 0x1043, 0x200F),
    "Exact ASUS RTL810xE PCI identity should pass");
Check(Realtek810xERegisterDecoder.IsExactAsusRtl810xE(
      0x10EC, 0x8136, 0x0006, 0x0200, 0x0080, 0x1043, 0x200F),
    "Multifunction bit must not invalidate a type-0 PCI header");
Check(!Realtek810xERegisterDecoder.IsExactAsusRtl810xE(
      0x10EC, 0x8136, 0x0006, 0x0200, 0x0001, 0x1043, 0x200F),
    "Type-1 bridge header must be rejected");
Check(!Realtek810xERegisterDecoder.IsExactAsusRtl810xE(
      0x10EC, 0x8136, 0x0106, 0x0200, 0x0000, 0x1043, 0x200F),
    "Unexpected PCI programming interface must be rejected");
Check(!Realtek810xERegisterDecoder.IsExactAsusRtl810xE(
      0x10EC, 0x8136, 0x0007, 0x0200, 0x0000, 0x1043, 0x200F),
    "Unreviewed silicon revision must be rejected");
Check(!Realtek810xERegisterDecoder.IsExactAsusRtl810xE(
      0x10EC, 0x8136, 0x0006, 0x0280, 0x0000, 0x1043, 0x200F),
    "Non-Ethernet PCI subclass must be rejected");
Check(!Realtek810xERegisterDecoder.IsExactAsusRtl810xE(
      0x10EC, 0x8136, 0x0006, 0x0200, 0x0000, 0x1043, 0xFFFF),
    "Unexpected subsystem must be rejected");
Check(!Realtek810xERegisterDecoder.IsExactAsusRtl810xE(
      0x10EC, 0x8168, 0x0006, 0x0200, 0x0000, 0x1043, 0x200F),
    "Gigabit Realtek device must be rejected");
Check(!Realtek810xERegisterDecoder.IsExactAsusRtl810xE(
      0xFFFF, 0x8136, 0x0006, 0x0200, 0x0000, 0x1043, 0x200F),
    "Invalid PCI vendor must be rejected");

// Pure RTL810xE register decoding only: no MMIO is performed by this test.
var station = new byte[] { 0x02, 0x11, 0x22, 0x33, 0x44, 0x55 };
Check(Realtek810xERegisterDecoder.StationAddressOffset == 0x00 &&
      Realtek810xERegisterDecoder.PhyStatusOffset == 0x6C &&
      Realtek810xERegisterDecoder.TxConfigOffset == 0x40,
    "RTL810xE station/PHY register offsets changed");
Check(Realtek810xERegisterDecoder.TryDecode(station, 0x0B, out var nicSnapshot) &&
      nicSnapshot.MacAddress == "02:11:22:33:44:55" &&
      nicSnapshot.Carrier && nicSnapshot.SpeedMbps == 100 &&
      nicSnapshot.FullDuplex && nicSnapshot.RawPhyStatus == 0x0B,
    "100M full-duplex PHY status or MAC decode failed");
Check(Realtek810xERegisterDecoder.TryDecode(station, 0x06, out nicSnapshot) &&
      nicSnapshot.Carrier && nicSnapshot.SpeedMbps == 10 && !nicSnapshot.FullDuplex,
    "10M half-duplex PHY status decode failed");
Check(Realtek810xERegisterDecoder.TryDecode(station, 0x08, out nicSnapshot) &&
      !nicSnapshot.Carrier && nicSnapshot.SpeedMbps == 0 && !nicSnapshot.FullDuplex,
    "No carrier must not invent a negotiated speed or duplex");
Check(!Realtek810xERegisterDecoder.TryDecode(station, 0x02, out nicSnapshot),
    "Link-up without a speed indication must fail closed");
Check(!Realtek810xERegisterDecoder.TryDecode(station, 0x0E, out nicSnapshot),
    "Conflicting 10M/100M bits must fail closed");
Check(!Realtek810xERegisterDecoder.TryDecode(station, 0x1A, out nicSnapshot),
    "Unexpected gigabit status must not be reported for RTL810xE");
Check(!Realtek810xERegisterDecoder.TryDecode(null, 0x0B, out nicSnapshot) &&
      !Realtek810xERegisterDecoder.TryDecode(new byte[5], 0x0B, out nicSnapshot) &&
      !Realtek810xERegisterDecoder.TryDecode(new byte[6], 0x0B, out nicSnapshot),
    "Missing, truncated or all-zero station address must fail closed");
Check(!Realtek810xERegisterDecoder.TryDecode(
      new byte[] { 0x01, 0, 0, 0, 0, 1 }, 0x0B, out nicSnapshot),
    "Multicast station address must fail closed");
// Two identical eleven-byte passes: reject changing MAC, PHY or TxConfig.
var registerReader = new FakeRealtekRegisterReader();
Check(Realtek810xERegisterSampler.TrySample(registerReader, out nicSnapshot) &&
      nicSnapshot.MacAddress == "02:11:22:33:44:55" &&
      nicSnapshot.Carrier && nicSnapshot.SpeedMbps == 100 &&
      nicSnapshot.FullDuplex && registerReader.ReadCount == 22 &&
      nicSnapshot.RawTxConfig == 0x44800000u &&
      nicSnapshot.Xid == 0x448 && nicSnapshot.ChipVariant == "RTL8106e",
    "RTL810xE register sampler must require two identical register passes");
Check(registerReader.ReadOffsets.Count == 22 &&
      registerReader.ReadOffsets[0] == 0 &&
      registerReader.ReadOffsets[5] == 5 &&
      registerReader.ReadOffsets[6] == 0x6C &&
      registerReader.ReadOffsets[7] == 0x40 &&
      registerReader.ReadOffsets[10] == 0x43 &&
      registerReader.ReadOffsets[11] == 0 &&
      registerReader.ReadOffsets[16] == 5 &&
      registerReader.ReadOffsets[17] == 0x6C &&
      registerReader.ReadOffsets[18] == 0x40 &&
      registerReader.ReadOffsets[21] == 0x43,
    "RTL810xE register sampler used unexpected offsets");
var changingPhy = new FakeRealtekRegisterReader { ChangePhyOnSecondPass = true };
Check(!Realtek810xERegisterSampler.TrySample(changingPhy, out nicSnapshot) &&
      nicSnapshot.MacAddress == null && changingPhy.ReadCount == 22,
    "Renegotiating PHY must not produce a successful snapshot");
var changingMac = new FakeRealtekRegisterReader { ChangeMacOnSecondPass = true };
Check(!Realtek810xERegisterSampler.TrySample(changingMac, out nicSnapshot) &&
      nicSnapshot.MacAddress == null,
    "Changing MAC must not produce a successful snapshot");
var changingXid = new FakeRealtekRegisterReader { ChangeTxConfigOnSecondPass = true };
Check(!Realtek810xERegisterSampler.TrySample(changingXid, out nicSnapshot) &&
      nicSnapshot.MacAddress == null,
    "Changing TxConfig must not produce a successful snapshot");
registerReader.ThrowAt = 3;
Check(!Realtek810xERegisterSampler.TrySample(registerReader, out nicSnapshot) &&
      nicSnapshot.MacAddress == null,
    "Failed register read must not expose a partial MAC");
registerReader.ThrowAt = 0x42;
Check(!Realtek810xERegisterSampler.TrySample(registerReader, out nicSnapshot) &&
      nicSnapshot.MacAddress == null,
    "Failed TxConfig byte read must not expose a partial snapshot");
registerReader.ThrowAt = -1;
registerReader.TxConfig = 0x40900000u;
Check(Realtek810xERegisterSampler.TrySample(registerReader, out nicSnapshot) &&
      nicSnapshot.Xid == 0x409 && nicSnapshot.ChipVariant == "RTL8105e",
    "RTL8105e exact XID classification failed");
registerReader.TxConfig = 0x40800000u;
Check(Realtek810xERegisterSampler.TrySample(registerReader, out nicSnapshot) &&
      nicSnapshot.Xid == 0x408 && nicSnapshot.ChipVariant == "RTL8105e",
    "RTL8105e masked XID classification failed");
registerReader.TxConfig = 0x12300000u;
Check(Realtek810xERegisterSampler.TrySample(registerReader, out nicSnapshot) &&
      nicSnapshot.ChipVariant == "unknown",
    "Unrecognised XID must not be assigned a supported chip variant");
registerReader.PhyStatus = 0x02; // link without a negotiated speed
Check(!Realtek810xERegisterSampler.TrySample(registerReader, out nicSnapshot),
    "Invalid live PHY status must fail closed");
Check(!Realtek810xERegisterSampler.TrySample(null, out nicSnapshot),
    "Null register window must fail closed");
Console.WriteLine("[NET3095-RTL810XE-READER] PASS C# bounded 22-read double snapshot and instability rejection; no hardware MMIO proof");

Console.WriteLine("[NET3095-RTL810XE-SNAPSHOT] PASS C# synthetic MAC/PHYstatus decoding; no physical MMIO/link proof");

Console.WriteLine("[NET3095-PCI-PM] PASS C# read-only PMCSR D3hot/PME flags and absent-PM guard; no power transition");
Console.WriteLine("[NET3095-PCI-MSIX-BAR] PASS C# MSI-X table/PBA range and overlap guards, high-half/IO/unassigned/overflow; no MMIO or IRQ proof");
Console.WriteLine("[NET3095-PCI-IRQ] PASS C# read-only MSI/MSI-X control, vector count, BAR indicators and malformed BAR guards; no IRQ delivery proof");
Console.WriteLine("[NET3095-PCI-CAPS] PASS C# read-only PM/MSI/MSI-X linked list, cycles, reserved IDs, pointers and fail-closed guards");
Console.WriteLine("[NET3095-PCI-BAR] PASS C# read-only MMIO32/IO/MMIO64, masking, header/identity guards and fail-closed BAR pairs");

Console.WriteLine("[NET3095-PCI-METADATA] PASS C# read-only revision/subsystem propagation, header guard and legacy compatibility");

static void Check(bool ok, string message)
{
    if (!ok) throw new Exception(message);
}


sealed class FakeE1000EPciConfig : IPciConfigAccessor
{
    public ushort VendorId { get; set; } = 0x8086;
    public ushort DeviceId { get; set; } = 0x10D3;
    public ushort Command { get; set; } = 0x0006;
    public uint Bar0Low { get; set; } = 0xFEBF0000u;
    public uint Bar0High { get; set; } = 0;
    public byte ClassCode { get; set; } = 0x02;
    public byte Subclass { get; set; } = 0x00;
    public byte ProgrammingInterface { get; set; } = 0x00;
    public byte HeaderType { get; set; } = 0x00;
    public byte IrqLine { get; set; } = 11;
    public byte IrqPin { get; set; } = 1;
    public ushort Status { get; set; }
    public byte CapabilityPointer { get; set; } = 0x40;
    public byte MsiId { get; set; } = 0x05;
    public byte MsiNext { get; set; } = 0x50;
    public byte MsiXNext { get; set; }
    public ushort MsiControl { get; set; }
    public ushort MsiXControl { get; set; }
    public byte PmId { get; set; } = 0x01;
    public byte PmNext { get; set; }
    public ushort PmControlStatus { get; set; }
    public byte TailCapabilityId { get; set; } = 0x05;
    public byte TailCapabilityNext { get; set; }
    public ushort TailControl { get; set; }
    public bool ChangePmStateOnSecondRead { get; set; }
    public bool ChangePmPmeOnSecondRead { get; set; }
    public bool ChangeStatusOnSecondRead { get; set; }
    public bool ChangeCapabilityPointerOnSecondRead { get; set; }
    public bool EnableMsiAfterFirstModeScan { get; set; }
    public bool ThrowOnRead { get; set; }
    public bool ChangeIrqOnSecondRead { get; set; }
    public bool ChangeIrqPinOnSecondRead { get; set; }
    public bool DisableIntxAfterIrqRead { get; set; }
    public bool ChangeDeviceOnSecondRead { get; set; }
    public bool ChangeClassOnSecondRead { get; set; }
    public bool ChangeCommandOnSecondRead { get; set; }
    public bool ChangeBarLowOnSecondRead { get; set; }
    public bool ChangeBarHighOnSecondRead { get; set; }
    public bool ChangeBarLowAfterFirstSnapshot { get; set; }
    public bool ChangeBarHighAfterFirstSnapshot { get; set; }
    public bool ChangeHeaderOnSecondRead { get; set; }
    private int deviceReads;
    private int classReads;
    private int commandReads;
    private int barLowReads;
    private int barHighReads;
    private int headerReads;
    private int irqLineReads;
    private int irqPinReads;
    private int statusReads;
    private int capabilityPointerReads;
    private int msiControlReads;
    private int pmControlReads;
    public int ReadCount { get; private set; }

    private void CheckTarget(byte bus, byte slot, byte function)
    {
        if (ThrowOnRead || bus != 0 || slot != 2 || function != 0)
            throw new InvalidOperationException("Invalid synthetic E1000E PCI read");
        ReadCount++;
    }

    private ushort ReadCommand()
    {
        commandReads++;
        if (DisableIntxAfterIrqRead && irqLineReads > 0)
            return (ushort)(Command | 0x0400);
        return ChangeCommandOnSecondRead && commandReads > 1
            ? (ushort)(Command ^ 0x0004) : Command;
    }

    private ushort ReadBarLow()
    {
        barLowReads++;
        return ((ChangeBarLowOnSecondRead && barLowReads > 1) ||
                (ChangeBarLowAfterFirstSnapshot && barLowReads > 2))
            ? (ushort)(Bar0Low ^ 0x1000u) : (ushort)Bar0Low;
    }

    private ushort ReadBarHigh()
    {
        barHighReads++;
        return ((ChangeBarHighOnSecondRead && barHighReads > 1) ||
                (ChangeBarHighAfterFirstSnapshot && barHighReads > 2))
            ? (ushort)(Bar0High ^ 1u) : (ushort)Bar0High;
    }

    public ushort Read16(byte bus, byte slot, byte function, byte offset)
    {
        CheckTarget(bus, slot, function);
        return offset switch
        {
            0x00 => VendorId,
            0x02 => ++deviceReads > 1 && ChangeDeviceOnSecondRead
                ? (ushort)(DeviceId ^ 1) : DeviceId,
            0x04 => ReadCommand(),
            0x06 => ++statusReads > 1 && ChangeStatusOnSecondRead
                ? (ushort)(Status ^ 0x0010) : Status,
            0x42 => ++msiControlReads > 1 && EnableMsiAfterFirstModeScan
                ? (ushort)(MsiControl | 1) : MsiControl,
            0x4A => MsiXControl, // malformed overlapping MSI-X test
            0x52 => MsiXControl,
            0xEA or 0xEE or 0xF2 or 0xF6 or 0xFA => TailControl,
            0x64 => ++pmControlReads > 1
                ? (ushort)(PmControlStatus ^
                    (ChangePmStateOnSecondRead ? 0x0003 : 0) ^
                    (ChangePmPmeOnSecondRead ? 0x8100 : 0))
                : PmControlStatus,
            0x10 => ReadBarLow(),
            0x12 => (ushort)(Bar0Low >> 16),
            0x14 => ReadBarHigh(),
            0x16 => (ushort)(Bar0High >> 16),
            _ => throw new InvalidOperationException("Unexpected E1000E PCI word offset")
        };
    }

    public byte Read8(byte bus, byte slot, byte function, byte offset)
    {
        CheckTarget(bus, slot, function);
        return offset switch
        {
            0x0B => ++classReads > 1 && ChangeClassOnSecondRead
                ? (byte)(ClassCode ^ 1) : ClassCode,
            0x0A => Subclass,
            0x09 => ProgrammingInterface,
            0x0E => ++headerReads > 1 && ChangeHeaderOnSecondRead
                ? (byte)(HeaderType ^ 1) : HeaderType,
            0x34 => ++capabilityPointerReads > 1 && ChangeCapabilityPointerOnSecondRead
                ? (byte)(CapabilityPointer ^ 4) : CapabilityPointer,
            0x40 => MsiId,
            0x41 => MsiNext,
            0x48 => (byte)0x11,
            0x49 => MsiXNext,
            0x50 => (byte)0x11, // valid non-overlapping MSI-X header
            0x51 => MsiXNext,
            0x58 => PmId, // malformed overlapping PM header
            0x59 => (byte)0,
            0x60 => PmId,
            0x61 => PmNext,
            0x68 => PmId, // second PM cap used to reject duplicates
            0x69 => (byte)0,
            0xE8 or 0xEC or 0xF0 or 0xF4 or 0xF8 => TailCapabilityId,
            0xE9 or 0xED or 0xF1 or 0xF5 or 0xF9 => TailCapabilityNext,
            0xFC => PmId,
            0xFD => PmNext,
            0x3C => ++irqLineReads > 1 && ChangeIrqOnSecondRead
                ? (byte)(IrqLine ^ 1) : IrqLine,
            0x3D => ++irqPinReads > 1 && ChangeIrqPinOnSecondRead
                ? (byte)(IrqPin ^ 1) : IrqPin,
            _ => throw new InvalidOperationException("Unexpected E1000E PCI byte offset")
        };
    }
}

sealed class FakePciConfig : IPciConfigAccessor
{
    public int SubsystemReads { get; private set; }

    public ushort Read16(byte bus, byte device, byte function, byte offset)
    {
        if (bus != 0 || function != 0) return 0xFFFF;
        if (offset == 0x00)
            return device switch { 0 => 0x8086, 2 => 0x10EC, 4 => 0x1234, _ => 0xFFFF };
        if (offset == 0x02)
            return device switch { 0 => 0x1234, 2 => 0x8136, 4 => 0x5678, _ => 0xFFFF };
        if (offset == 0x2C || offset == 0x2E)
        {
            if (device != 2) throw new Exception("Subsystem read outside type-0 network header");
            SubsystemReads++;
            return offset == 0x2C ? (ushort)0x1043 : (ushort)0x200F;
        }
        throw new Exception($"Unexpected PCI Read16 offset 0x{offset:X2}");
    }

    public byte Read8(byte bus, byte device, byte function, byte offset)
    {
        if (bus != 0 || function != 0 || (device != 0 && device != 2 && device != 4))
            throw new Exception("Unexpected PCI Read8 target");
        return offset switch
        {
            0x08 => device == 2 ? (byte)0x06 : (byte)0x04,
            0x09 => 0,
            0x0A => 0,
            0x0B => device == 0 ? (byte)0x06 : (byte)0x02,
            0x0E => device == 4 ? (byte)0x01 : (byte)0x00,
            _ => throw new Exception($"Unexpected PCI Read8 offset 0x{offset:X2}")
        };
    }
}

// Read-only synthetic PCI config. No write API exists on IPciConfigAccessor.
sealed class FakeBarConfig : IPciConfigAccessor
{
    private readonly uint[] values = new uint[6];
    private readonly System.Collections.Generic.Dictionary<byte, (byte Id, byte Next)> caps = new();
    public byte CapabilityPointer { get; set; } = 0x40;
    public bool CapabilityListPresent { get; set; } = true;
    public int CapabilityReads { get; private set; }
    public void SetCapability(byte offset, byte id, byte next) => caps[offset] = (id, next);
    private ushort pmcsr;
    public void SetPmcsr(ushort value) => pmcsr = value;
    private ushort msiControl;
    private ushort msixControl;
    private uint msixTable;
    private uint msixPba;
    public void SetMsiControl(ushort value) => msiControl = value;
    public void SetMsixControl(ushort value) => msixControl = value;
    public void SetMsixTable(uint table, uint pba) { msixTable = table; msixPba = pba; }
    public ushort VendorId { get; set; } = 0x10EC;
    public byte HeaderType { get; set; }
    public int BarReads { get; private set; }
    public void SetBar(int index, uint value) => values[index] = value;

    public byte Read8(byte bus, byte device, byte function, byte offset)
    {
        if (bus != 0 || device != 2 || function != 0) throw new Exception("Unexpected BDF");
        if (offset == 0x34)
        {
            CapabilityReads++;
            return CapabilityPointer;
        }
        byte aligned = (byte)(offset & 0xFC);
        if (caps.TryGetValue(aligned, out var entry) &&
            (offset == aligned || offset == aligned + 1))
        {
            CapabilityReads++;
            return offset == aligned ? entry.Id : entry.Next;
        }
        return offset switch
        {
            0x0B => 0x02,
            0x0A => 0x00,
            0x0E => HeaderType,
            0x3C => 11,
            0x3D => 1,
            _ => throw new Exception("Unexpected byte config read")
        };
    }

    public ushort Read16(byte bus, byte device, byte function, byte offset)
    {
        if (bus != 0 || device != 2 || function != 0) throw new Exception("Unexpected BDF");
        if (offset == 0x00) return VendorId;
        if (offset == 0x02) return 0x8136;
        if (offset == 0x04) return 0x0007;
        if (offset == 0x06) return CapabilityListPresent ? (ushort)0x0010 : (ushort)0;
        if (offset == 0x44) return pmcsr;
        if (offset == 0x52) return msiControl;
        if (offset == 0x62) return msixControl;
        if (offset >= 0x64 && offset <= 0x6A && (offset & 1) == 0)
        {
            uint word = offset < 0x68 ? msixTable : msixPba;
            return (offset & 2) != 0 ? (ushort)(word >> 16) : (ushort)word;
        }
        if (offset < 0x10 || offset > 0x26 || (offset & 1) != 0)
            throw new Exception("Unexpected BAR offset");
        BarReads++;
        uint value = values[(offset - 0x10) / 4];
        return (offset & 2) != 0 ? (ushort)(value >> 16) : (ushort)value;
    }
}

sealed class FakeRealtekRegisterReader : IRealtek810xEByteReader
{
    public int ReadCount { get; private set; }
    public int ThrowAt { get; set; } = -1;
    public byte PhyStatus { get; set; } = 0x0B;
    public uint TxConfig { get; set; } = 0x44800000u;
    public bool ChangePhyOnSecondPass { get; set; }
    public bool ChangeMacOnSecondPass { get; set; }
    public bool ChangeTxConfigOnSecondPass { get; set; }
    public System.Collections.Generic.List<ushort> ReadOffsets { get; } = new();
    public byte Read8(ushort offset)
    {
        ReadCount++;
        ReadOffsets.Add(offset);
        if (offset == ThrowAt) throw new InvalidOperationException("synthetic read failure");
        if (offset == 0x6C)
            return ChangePhyOnSecondPass && ReadCount > 11 ? (byte)(PhyStatus ^ 0x01) : PhyStatus;
        if (offset >= 0x40 && offset <= 0x43)
        {
            uint tx = ChangeTxConfigOnSecondPass && ReadCount > 11 ?
                TxConfig ^ 0x00100000u : TxConfig;
            return (byte)(tx >> (8 * (offset - 0x40)));
        }
        byte[] mac = { 0x02, 0x11, 0x22, 0x33, 0x44, 0x55 };
        if (offset < 6)
            return ChangeMacOnSecondPass && ReadCount > 11 && offset == 0 ?
                (byte)(mac[0] ^ 0x02) : mac[offset];
        throw new InvalidOperationException("unexpected MMIO offset");
    }
}
