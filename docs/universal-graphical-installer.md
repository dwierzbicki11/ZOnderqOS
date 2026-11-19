# ZonderqOS Gen3 — Universal Graphical OS Installer & Architecture Audit

## 1. Architecture Audit (Stage 1)

### 1.1 Storage & Boot Pipeline in Cosmos Gen3 3.0.97
- **Block Device Discovery (`Cosmos.Kernel.System.Storage.StorageManager`)**:
  - Hardware block drivers included in stock Cosmos 3.0.97:
    - `AhciDriver` (PCI SATA / AHCI controllers on physical hardware, QEMU `ich9-ahci`, VirtualBox SATA, VMware SATA).
    - `NvmeDriver` (PCIe NVMe controllers).
    - `UsbMassStorageDriver` (USB flash drives and external disks via xHCI/EHCI/OHCI).
    - `VirtioBlkDriver` (VirtIO block devices in QEMU/KVM).
  - Stock Cosmos 3.0.97 does **not** include an ISO9660 filesystem driver or legacy ATAPI CD-ROM block driver. To make installation from any boot medium (including optical ISO CD-ROMs in VirtualBox, VMware, and QEMU) 100% self-contained:
    1. `LimineBootMediaDetector` registers a `LimineExecutableFileRequest` (`0xad97e90e83f1ed67, 0x31eb5d1c5ff23b69`) in the ELF `.data` section, receiving the direct HHDM memory pointer and byte size of the booted `ZonderqOS.elf` image from Limine at boot time.
    2. Verified bootloader binaries (`bootguard.efi`, `LIMINE.EFI`, `limine-bios.sys`, and `limine-hdd.bin`) are embedded directly inside `ZonderqOS.elf` as manifest resources (`634 KB` total).
- **Live / Installer Ephemeral RAM Environment (`Disk.cs` & `RamRootVolume.cs`)**:
  - When `ZonderqOS.iso` boots on a machine without an existing `ZONDERQDATA` volume (or when `/ZonderqOS Live & Installer` with `cmdline: live` is chosen in Limine), `Disk.Initialize()` automatically mounts the 64 MiB private in-memory `RamRootVolume` at `/` (`Disk.IsLiveSession = true`, `Disk.IsVerifiedPersistentDataRootMounted = false`).
  - Zero writes are performed to any physical disk during Live boot.
  - `GuiManager` automatically opens `ZonderqOS Installer` on the Live desktop, allowing the user either to click **Install ZonderqOS** or **Try ZonderqOS without installing**.
- **Partitioning, Formatting & Bootloader Layout (`DiskFormattingEngine.cs` & `SystemInstallerEngine.cs`)**:
  - **Default Dual-Partition Layout (disks >= 160 MB)**:
    - **MBR + Limine BIOS Stage 1/2** in LBA `0..43` (preserving disk signature and partition table).
    - **Partition 1 — EFI System Partition (`64 MB`, LBA `2048..133119`, MBR type `0xEF`, FAT32 label `ZONDERQEFI`)**:
      - `/EFI/BOOT/BOOTX64.EFI` (ZonderqOS EFI BootGuard)
      - `/EFI/BOOT/LIMINE.EFI` (Limine UEFI bootloader)
      - `/boot/limine/limine.conf`, `/boot/limine.conf`, `/limine.conf`
      - `/boot/limine/limine-bios.sys`
    - **Partition 2 — ZonderqOS System Partition (LBA `133120..End`, MBR type `0x0C` active `0x80`, FAT32 label `ZONDERQDATA`)**:
      - `/boot/slot_a/ZonderqOS.elf` and `/boot/slot_a/kernel.sha256`
      - `/boot/slot_b/` (prepared for A/B updates)
      - `/boot/boot_state.cfg` (`ACTIVE_SLOT=A`, `FALLBACK_SLOT=B`, `BOOTGUARD_PREBOOT=0`, `SLOT_A_STATUS=Healthy`)
      - `/EFI/BOOT/BOOTX64.EFI`, `/EFI/BOOT/LIMINE.EFI`, `/boot/limine/limine-bios.sys`, `/boot/limine/limine.conf`
      - `/system`, `/etc`, `/etc/zonderq/settings.conf`, `/etc/passwd`, `/etc/shadow` (PBKDF2-HMAC-SHA256 `v2$4096$`), `/etc/hostname`, `/usr`, `/home/<username>`, `/var/log`
- **Multi-Layer Data Safety & Target Verification (`DiskCatalog.cs` & `DiskIdentityToken.cs`)**:
  - The installer never auto-selects a disk.
  - Installation media (detected via ISO9660 PVD `"CD001"` at offset `32768`, read-only media flag, or Limine `MbrDiskId`) and active system disks (hosting `/` or any mounted VFS volume) are strictly blocked from formatting.
  - Before erasing a disk, the user must review the disk's model, capacity, and existing partitions, and explicitly type the device identifier (e.g. `DISK-0` or `DISK-1`).
  - Immediately prior to writing, `DiskCatalog.VerifyIdentityBeforeWrite` re-verifies the `IBlockDevice` object reference, capacity, MBR signature, and SHA-256 digests of LBA 0 and LBA 1.

---

## 2. Installation Guide

### 2.1 Install on Physical Hardware
1. Flash `ZonderqOS.iso` onto a USB flash drive on Linux/Windows/macOS (e.g., using `dd if=ZonderqOS.iso of=/dev/sdX bs=4M status=progress oflag=sync` or Rufus in DD mode).
2. Connect the USB drive and a dedicated target SATA/AHCI or USB disk to the computer.
3. Boot from the USB drive in UEFI (with Secure Boot disabled) or Legacy BIOS/CSM mode.
4. ZonderqOS boots into the **Live RAM Environment** and opens **ZonderqOS Installer**:
   - **Screen 1 (Welcome)**: Click **Install ZonderqOS** (or **Try ZonderqOS without installing** to explore the Live desktop first).
   - **Screen 2 (System Requirements)**: Verify CPU architecture (`x86_64`), RAM, BIOS/UEFI firmware mode, and detected storage disks. Click **Next**.
   - **Screen 3 (Select Installation Disk)**: Select the target disk (e.g. `DISK-1`). Verify its model, capacity, interface, and existing partitions. The active USB installation media is marked `INSTALL MEDIA (Protected)` and cannot be formatted. Click **Next**.
   - **Screen 4 (Installation Type)**: Choose **Erase Disk and Install ZonderqOS** (default automatic dual-partition setup) or **Advanced Installation**. Click **Continue**.
   - **Disk Erasing Warning Dialog**: Carefully verify the selected disk details, type the exact disk identifier (e.g. `DISK-1`), and click **Erase Disk and Install**.
   - **Screen 5 (User Setup)**: Enter your desired **Username**, **Computer Name**, **Password**, and **Confirm Password**, then click **Install Now**.
   - **Screen 6 (Installation Progress)**: Watch real-time progress across all 9 stages (`Preparing disk` through `Finishing installation`).
   - **Screen 7 (Installation Complete)**: Once `ZonderqOS has been successfully installed.` appears, disconnect the USB installer drive and click **Restart Now**.

### 2.2 Install on Oracle VirtualBox
1. Create a new Virtual Machine:
   - **Type**: `Other` / **Version**: `Other/Unknown (64-bit)`
   - **Memory**: `2048 MB` RAM (`1 vCPU`)
   - **Firmware**: Enable EFI (`System -> Motherboard -> Enable EFI`, Secure Boot disabled) or Legacy BIOS
   - **Hard Disk**: Create a new Virtual Hard Disk (`VDI`, `8 GB` or larger) attached to the **SATA (AHCI)** controller
   - **Optical Drive**: Attach `ZonderqOS.iso` to the IDE or SATA Optical Drive
2. Start the VM. ZonderqOS boots directly into the Live GUI Installer without requiring any pre-formatted partition.
3. Follow the 7-screen graphical wizard to install onto `DISK-0`.
4. After **Installation Complete**, remove `ZonderqOS.iso` from the virtual optical drive and click **Restart Now**.

### 2.3 Install on VMware Workstation / Player
1. Create a new Custom Virtual Machine (`Other 64-bit`, `1 vCPU`, `2 GB` RAM).
2. Select **UEFI** firmware (with Secure Boot unchecked) or **BIOS**.
3. Create a new **SATA** virtual disk (`8 GB` VMDK).
4. Attach `ZonderqOS.iso` to the CD/DVD drive and boot the VM.
5. Complete the graphical installation wizard, disconnect the ISO from the CD/DVD drive, and restart.

### 2.4 Install on QEMU / KVM
1. Create an empty virtual disk image:
   ```bash
   truncate -s 8G zonderq-disk.img
   ```
2. Boot the installer ISO with the empty disk attached to AHCI:
   ```bash
   qemu-system-x86_64 -M q35 -m 2048M -smp 1 -vga std \
     -device qemu-xhci,id=xhci -device usb-kbd,bus=xhci.0 -device usb-tablet,bus=xhci.0 \
     -drive file=output-x64/ZonderqOS.iso,if=none,id=cd0,format=raw,readonly=on \
     -device ide-cd,drive=cd0,bootindex=0 -boot d \
     -device ich9-ahci,id=ahci0 \
     -drive file=zonderq-disk.img,if=none,id=hd0,format=raw \
     -device ide-hd,drive=hd0,bus=ahci0.0
   ```
3. Complete the graphical installation onto `DISK-0`, then boot directly from `zonderq-disk.img` without the ISO (either in Legacy BIOS or with OVMF UEFI `-drive if=pflash,format=raw,readonly=on,file=/usr/share/OVMF/OVMF_CODE_4M.fd`).

---

## 3. Disk Erasing Warning & Safety Policy

> **WARNING: ALL DATA ON THE SELECTED TARGET DISK WILL BE DELETED**
>
> Selecting **Erase Disk and Install ZonderqOS** removes all existing partitions, operating systems, applications, and files on the chosen target disk.
>
> - Always verify the **Disk Model**, **Capacity**, **Existing Partitions**, and **Device Identifier** (`DISK-X`) before typing the confirmation ID.
> - Dual-boot installation alongside an existing OS on the *same* physical disk is not enabled by default; always use a dedicated physical or virtual disk for ZonderqOS.
> - Other connected disks (such as a primary SSD with Linux or Windows) are strictly isolated by hardware object reference and LBA 0/1 SHA-256 identity tokens.

---

## 4. Troubleshooting

- **No eligible target disk appears in Screen 3**:
  - Ensure your virtual or physical disk is attached to a supported controller (**SATA / AHCI**, **NVMe**, **VirtIO Block**, or **USB Mass Storage**) and has at least **68 MB** capacity (8 GB recommended). Legacy IDE (`piix3-ide`) hard disks should be switched to **SATA (AHCI)** in VirtualBox/VMware/QEMU.
- **Selected disk shows `Protected: Active installation media` or `Protected: Active system disk (/)`**:
  - ZonderqOS blocks formatting the drive from which the system is currently running. Select a secondary unmounted disk.
- **System boots back into the installer after installation**:
  - Disconnect or eject `ZonderqOS.iso` / the USB installer drive so UEFI/BIOS boots from the newly installed disk.
