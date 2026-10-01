# ZOnderqOS System Installer Roadmap

The installer is implemented fail-closed. No destructive action is allowed until the
kernel can prove which physical block device backs the currently mounted root filesystem.

## I0 - target discovery and dry-run

Status: implemented on the installer foundation branch.

- `install scan`
- `install plan <disk_id>`
- deterministic MBR/FAT32 layout policy
- no disk writes
- `install apply` explicitly blocked

## I1 - root device identity

Required before any destructive installation.

- expose mounted root -> partition -> physical block-device identity
- reject the running/root disk unless installation is running from a known live/read-only source
- test identity across one-disk and two-disk QEMU setups

## I2 - target preparation

- explicit confirmation token
- create partition table
- rescan partitions without requiring an unsafe blind reboot path
- format target FAT32
- verify mount/read/write on the target

## I3 - system payload deployment

- copy the kernel/system payload from a known immutable install source
- create FHS hierarchy
- install default /etc configuration
- create first-boot marker
- verify file counts/sizes and required files

## I4 - bootloader

- install/copy Limine boot files and generated configuration
- verify bootloader files before marking install complete

## I5 - first-boot verification

- boot installed disk in QEMU
- verify root mount, settings, users, services and GUI startup
- installer marker is cleared only after successful first boot

## I6 - GUI installer

A GUI wizard may wrap the same backend only after I1-I5 are proven. The GUI must not
contain a second partitioning/install implementation.
