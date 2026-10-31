# RTL810xE 10EC:8136 read-only hardware snapshot (Cosmos Gen3 3.0.95)

This is an **opt-in, serial-only physical diagnostic**, not a network driver.
The normal disk-backed GUI and its FAT32 ZONDERQDATA policy are unchanged.
No DHCP, frame TX/RX, DMA buffers, IRQ handler, filesystem mount or disk write
is performed by this diagnostic. It does not publish an Ethernet interface.

## Hardware scope

The DriverKit probe matches **only** PCI vendor/device `10EC:8136`,
revision `06`, subsystem `1043:200F`, Ethernet class/subclass `02:00`.
These identifiers were reported by the ASUS X540LA laptop. Other revisions
and machines are intentionally declined until their register layout is
reviewed. The diagnostic reads only BAR2 offsets `0x00..0x05` (MAC)
`0x6C` (PHYstatus), and `0x40..0x43` (32-bit little-endian TxConfig),
via the stock Cosmos 3.0.95
`DeviceBinding.MapRegisters(2)` and bounds-checked `RegisterWindow.Read8`.

Both the raw PHYstatus byte and its decoded carrier/speed/duplex
are emitted in the driver and kernel serial records. The log validator
rejects mismatches between the raw PHY byte and the decoded state;
these are still guest-reported observations, not an independent
hardware trace.

The read-only TxConfig XID is derived as `(TxConfig >> 20) & 0xFCF`,
following Linux r8169. Only the reviewed RTL8105e and RTL8106e
mask patterns are labelled; all other values are reported as `unknown`,
not silently treated as compatible. PCI revision `06` does **not** by
itself establish a specific RTL810xE silicon variant. This remains
identification metadata, not a transmit or DMA proof.

The probe checks PCI memory decoding, D0 (if a PM capability is present),
an assigned non-I/O BAR2, and an aperture of at least `0x6D` bytes.
It never enables memory decoding, bus mastering, DMA or interrupts itself.
**The upstream DriverKit may still quiesce and restore PCI configuration**
as part of normal probe arbitration; do not mistake this for a guarantee
that every PCI config register is never written.

The probe intentionally returns `Declined` after the snapshot. The
DriverKit releases the mapping and does not retain a bound network device.

## Build (isolated, never the production GUI)

```bash
bash tools/build-cosmos-3095-local.sh network-rtl-readonly
# output-3095/ZonderqOS-3095-network-rtl-readonly.iso
```

The script pins the exact upstream Cosmos tag `v3.0.95` and uses its
private cache. This ISO must be booted on a separately prepared test
medium, not written to an existing ZONDERQDATA, EFI, Kali or host partition.
The project intentionally does not flash a USB device.

On a laptop with serial console access, retain the **original boot log**.
A real snapshot is reported only after the DriverKit reads the eleven
register bytes (six MAC, one PHYstatus, four TxConfig) **twice** and
confirms identical MAC, PHYstatus and TxConfig values. Any disagreement
or read exception fails closed; the two passes cannot guarantee a link
will remain stable afterward. Both serial records include `samples=2`. Validate the serial evidence with:

```bash
python3 tests/verify_3095_realtek_readonly.py laptop-serial.log
```

The `--self-test` mode tests the parser with synthetic strings; it is
**not** evidence of a physical read. A missing BAR, disabled memory
decoding, unexpected PCI identity, invalid MAC or PHY status produces
a failure/decline rather than a false PASS. If the DriverKit probe is offered
but PCI configuration access, BAR mapping or MMIO sampling throws, the
isolated kernel now reports `[NET3095-RTL810XE][FAIL]` with a bounded
`reason=PCI/MMIO probe exception: <type>` instead of crashing the diagnostic
or counting a successful sample. Disabled memory decoding, D-state and
unassigned BAR2 have separate decline reasons. No sample is counted until
its serial diagnostic marker has been emitted successfully.

The laptop must actually expose a usable serial capture path to collect
the log. A QEMU serial log or a photograph of an unrelated boot cannot
substitute for the ASUS hardware run. The validator also rejects a log
containing kernel panic, fatal exception, page fault or a broken heartbeat
sequence, even if earlier sample markers appear valid.

## Verification limits

- C# smoke tests use a fake register window and check exact offsets,
  22 reads in two identical passes, changing MAC/PHY/TxConfig, invalid
  PHY status, RTL8105e/RTL8106e/unknown XIDs,
  little-endian TxConfig assembly and read failures.
- GitHub Actions compiles the isolated ISO and runs the synthetic
  parser gates. QEMU has no equivalent RTL810xE 10EC:8136 model, so
  QEMU cannot prove the physical MAC/link.
- The laptop needs a real boot and captured serial log. Even a successful
  read proves only the reported register snapshot, **not** link traffic,
  DMA, interrupt delivery, ARP, IPv4, DHCP, TCP or HTTPS.
- The separate E1000E QEMU N1–N6 tests continue to prove only the
  corresponding captured QEMU network exchanges.
