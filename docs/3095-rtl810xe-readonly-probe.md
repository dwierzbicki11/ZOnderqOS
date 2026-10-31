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
and `0x6C` (PHYstatus), via the stock Cosmos 3.0.95
`DeviceBinding.MapRegisters(2)` and bounds-checked `RegisterWindow.Read8`.

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
A real snapshot is reported only after the DriverKit reads the seven
register bytes. Validate the serial evidence with:

```bash
python3 tests/verify_3095_realtek_readonly.py laptop-serial.log
```

The `--self-test` mode tests the parser with synthetic strings; it is
**not** evidence of a physical read. A missing BAR, disabled memory
decoding, unexpected PCI identity, invalid MAC or PHY status produces
a failure/decline rather than a false PASS.

## Verification limits

- C# smoke tests use a fake register window and check exact offsets,
  seven reads, invalid PHY status and read failures.
- GitHub Actions compiles the isolated ISO and runs the synthetic
  parser gates. QEMU has no equivalent RTL810xE 10EC:8136 model, so
  QEMU cannot prove the physical MAC/link.
- The laptop needs a real boot and captured serial log. Even a successful
  read proves only the reported register snapshot, **not** link traffic,
  DMA, interrupt delivery, ARP, IPv4, DHCP, TCP or HTTPS.
- The separate E1000E QEMU N1–N6 tests continue to prove only the
  corresponding captured QEMU network exchanges.
