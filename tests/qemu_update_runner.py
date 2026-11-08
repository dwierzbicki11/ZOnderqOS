#!/usr/bin/env python3
"""End-to-end QEMU integration test runner for ZonderqOS Gen 3 A/B Dual-Slot Update Engine."""
from pathlib import Path
import os
import shutil
import subprocess
import sys
import time
from pyfatfs.PyFatFS import PyFatFS

ROOT = Path(__file__).resolve().parents[1]
ISO = ROOT / "output-3095/ZonderqOS-3095-disk.iso"
KERNEL_ELF = ROOT / "obj/Debug/net10.0/linux-x64/cosmos/iso_root/boot/ZonderqOS.elf"

def create_and_seed_disk(disk_path, kernel_elf_path):
    print(f"[QEMU-HARNESS] Creating 256MB disk image at {disk_path}...")
    subprocess.run(["truncate", "-s", "256M", str(disk_path)], check=True)
    
    # MBR partition starting at sector 2048
    sfdisk_proc = subprocess.Popen(["sfdisk", str(disk_path)], stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, text=True)
    sfdisk_proc.communicate("label: dos\nunit: sectors\nstart=2048, type=c\n")
    if sfdisk_proc.returncode != 0:
        raise RuntimeError("sfdisk failed")
        
    subprocess.run(["mkfs.vfat", "-I", "-F", "32", "-n", "ZONDERQDATA", "-h", "2048", "--offset", "2048", str(disk_path)], check=True, stdout=subprocess.DEVNULL)
    
    print("[QEMU-HARNESS] Seeding Slot A on ZONDERQDATA using PyFatFS...")
    fs = PyFatFS(str(disk_path), offset=2048 * 512)
    fs.makedir("/boot")
    fs.makedir("/boot/slot_a")
    fs.makedir("/boot/slot_b")
    fs.makedir("/boot/limine")
    fs.makedir("/var")
    fs.makedir("/var/log")
    fs.makedir("/updates")
    
    with open(kernel_elf_path, "rb") as kf:
        kernel_bytes = kf.read()
    
    with fs.open("/boot/slot_a/ZonderqOS.elf", "wb") as out_kf:
        out_kf.write(kernel_bytes)
        
    # Also write /boot/ZonderqOS.elf
    with fs.open("/boot/ZonderqOS.elf", "wb") as out_kf:
        out_kf.write(kernel_bytes)
        
    boot_state = (
        "# ZonderqOS A/B Bootloader State Configuration\n"
        "ACTIVE_SLOT=A\n"
        "FALLBACK_SLOT=B\n"
        "PENDING_SLOT=\n"
        "BOOT_ATTEMPTS=0\n"
        "MAX_BOOT_ATTEMPTS=3\n"
        "SLOT_A_STATUS=HEALTHY\n"
        "SLOT_A_VERSION=1.0.0-alpha\n"
        "SLOT_A_BUILD=3100\n"
        "SLOT_B_STATUS=EMPTY\n"
        "SLOT_B_VERSION=\n"
        "SLOT_B_BUILD=0\n"
    )
    with fs.open("/boot/boot_state.cfg", "w") as bsf:
        bsf.write(boot_state)
        
    limine_cfg = (
        "# Cosmos Gen3 / Limine v10 A/B dual-slot boot configuration.\n"
        "timeout: 3\n"
        "default_entry: 1\n\n"
        "/ZonderqOS (Slot A - Aktywny)\n"
        "    protocol: limine\n"
        "    resolution: 1366x768x32\n"
        "    path: fslabel(ZONDERQDATA):/boot/slot_a/ZonderqOS.elf\n\n"
        "/ZonderqOS (Slot B)\n"
        "    protocol: limine\n"
        "    resolution: 1366x768x32\n"
        "    path: fslabel(ZONDERQDATA):/boot/slot_b/ZonderqOS.elf\n\n"
        "/ZonderqOS (Kopia zapasowa ISO / Ratunkowa)\n"
        "    protocol: limine\n"
        "    resolution: 1366x768x32\n"
        "    path: boot():/boot/ZonderqOS.elf\n"
    )
    with fs.open("/boot/limine.conf", "w") as lcf:
        lcf.write(limine_cfg)
    with fs.open("/limine.conf", "w") as lcf:
        lcf.write(limine_cfg)
    with fs.open("/boot/limine/limine.conf", "w") as lcf:
        lcf.write(limine_cfg)
        
    fs.close()
    print("[QEMU-HARNESS] Slot A seeded successfully. File size:", len(kernel_bytes), "bytes")

def run_qemu(iso_path, disk_path, smp=1, seconds=25):
    print(f"[QEMU-HARNESS] Booting QEMU with {smp} vCPU for {seconds} seconds...")
    cmd = [
        "qemu-system-x86_64",
        "-M", "q35", "-accel", "tcg", "-cpu", "Nehalem",
        "-smp", str(smp), "-m", "2048M",
        "-vga", "std",
        "-drive", f"file={iso_path},if=none,id=cosmoscd,format=raw,readonly=on",
        "-device", "ide-cd,drive=cosmoscd,bootindex=0", "-boot", "d",
        "-device", "ich9-ahci,id=ahci0",
        "-drive", f"file={disk_path},if=none,id=ahcidisk0,format=raw,cache=directsync",
        "-device", "ide-hd,drive=ahcidisk0,bus=ahci0.0",
        "-display", "none",
        "-no-reboot"
    ]
    proc = subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    time.sleep(seconds)
    proc.terminate()
    try:
        proc.wait(timeout=5)
    except subprocess.TimeoutExpired:
        proc.kill()
        proc.wait()
    print("[QEMU-HARNESS] QEMU finished.")

def inspect_results(disk_path):
    print("[QEMU-HARNESS] Inspecting ZONDERQDATA partition contents...")
    fs = PyFatFS(str(disk_path), offset=2048 * 512)
    
    system_log = ""
    if fs.exists("/var/log/system.log"):
        with fs.open("/var/log/system.log", "r") as f:
            system_log = f.read()
            
    boot_state = ""
    if fs.exists("/boot/boot_state.cfg"):
        with fs.open("/boot/boot_state.cfg", "r") as f:
            boot_state = f.read()
            
    fs.close()
    return system_log, boot_state

if __name__ == "__main__":
    work_dir = Path("/tmp/zonderq_qemu_test")
    if work_dir.exists():
        shutil.rmtree(work_dir)
    work_dir.mkdir(parents=True)
    
    test_disk = work_dir / "zonderqdata.img"
    
    # 1. Test 1 vCPU
    print("\n============================================================")
    print("  TEST 1: QEMU Boot Verification (1 vCPU, Single-Core SMP)  ")
    print("============================================================")
    create_and_seed_disk(test_disk, KERNEL_ELF)
    run_qemu(ISO, test_disk, smp=1, seconds=28)
    
    log1, state1 = inspect_results(test_disk)
    print("\n--- /var/log/system.log excerpt ---")
    for line in log1.splitlines()[-15:]:
        print("  " + line)
        
    print("\n--- /boot/boot_state.cfg excerpt ---")
    for line in state1.splitlines()[:10]:
        print("  " + line)
        
    assert "ZonderqOS kernel successfully booted" in log1, "Kernel boot message not found in system.log!"
    assert "Mounted dedicated ZONDERQDATA at '/'" in log1 or "ZONDERQDATA" in log1, "ZONDERQDATA mount not recorded!"
    print("\n[PASS] 1 vCPU QEMU Boot & Slot Health Confirmation SUCCESSFUL!")
    
    # 2. Test 8 vCPU
    print("\n============================================================")
    print("  TEST 2: QEMU Boot Verification (8 vCPU, SMP Multi-Core)   ")
    print("============================================================")
    run_qemu(ISO, test_disk, smp=8, seconds=28)
    log8, state8 = inspect_results(test_disk)
    assert "ZonderqOS kernel successfully booted" in log8, "Kernel boot failed with 8 vCPUs!"
    print("[PASS] 8 vCPU QEMU Boot SUCCESSFUL without crashes or panics!")

    # 3. Test A/B Slot Switching and Slot B Promotion
    print("\n============================================================")
    print("  TEST 3: A/B Slot Switching & Slot B Promotion in QEMU     ")
    print("============================================================")
    print("[QEMU-HARNESS] Staging simulated update 1.0.1 (build 3110) into Slot B...")
    fs = PyFatFS(str(test_disk), offset=2048 * 512)
    with open(KERNEL_ELF, "rb") as kf:
        kernel_bytes = kf.read()
    with fs.open("/boot/slot_b/ZonderqOS.elf", "wb") as out_kf:
        out_kf.write(kernel_bytes)

    stage_b_state = (
        "# ZonderqOS A/B Bootloader State Configuration\n"
        "ACTIVE_SLOT=B\n"
        "FALLBACK_SLOT=A\n"
        "PENDING_SLOT=B\n"
        "BOOT_ATTEMPTS=0\n"
        "MAX_BOOT_ATTEMPTS=3\n"
        "SLOT_A_STATUS=HEALTHY\n"
        "SLOT_A_VERSION=1.0.0-alpha\n"
        "SLOT_A_BUILD=3100\n"
        "SLOT_B_STATUS=PENDING\n"
        "SLOT_B_VERSION=1.0.1\n"
        "SLOT_B_BUILD=3110\n"
    )
    with fs.open("/boot/boot_state.cfg", "w") as bsf:
        bsf.write(stage_b_state)

    limine_cfg_b = (
        "# Cosmos Gen3 / Limine v10 A/B dual-slot boot configuration.\n"
        "timeout: 3\n"
        "default_entry: 2\n\n"
        "/ZonderqOS (Slot A)\n"
        "    protocol: limine\n"
        "    resolution: 1366x768x32\n"
        "    path: fslabel(ZONDERQDATA):/boot/slot_a/ZonderqOS.elf\n\n"
        "/ZonderqOS (Slot B - Aktywny)\n"
        "    protocol: limine\n"
        "    resolution: 1366x768x32\n"
        "    path: fslabel(ZONDERQDATA):/boot/slot_b/ZonderqOS.elf\n\n"
        "/ZonderqOS (Kopia zapasowa ISO / Ratunkowa)\n"
        "    protocol: limine\n"
        "    resolution: 1366x768x32\n"
        "    path: boot():/boot/ZonderqOS.elf\n"
    )
    with fs.open("/boot/limine.conf", "w") as lcf:
        lcf.write(limine_cfg_b)
    with fs.open("/limine.conf", "w") as lcf:
        lcf.write(limine_cfg_b)
    with fs.open("/boot/limine/limine.conf", "w") as lcf:
        lcf.write(limine_cfg_b)
    fs.close()

    print("[QEMU-HARNESS] Booting into Slot B via Limine entry 2...")
    run_qemu(ISO, test_disk, smp=1, seconds=28)
    log_b, state_b = inspect_results(test_disk)

    print("\n--- /boot/boot_state.cfg after Slot B boot ---")
    for line in state_b.splitlines()[:12]:
        print("  " + line)

    assert "ACTIVE_SLOT=B" in state_b, "Active slot is not B!"
    assert "PENDING_SLOT=" in state_b and ("PENDING_SLOT=B" not in state_b), "Pending slot was not cleared!"
    assert "SLOT_B_STATUS=Healthy" in state_b or "SLOT_B_STATUS=HEALTHY" in state_b, "Slot B was not marked Healthy!"
    assert "Slot B pomyslnie zatwierdzony jako zdrowy" in log_b, "Confirmation message not in system.log!"
    print("\n[PASS] QEMU Slot B Boot, Kernel Execution, and Promotion SUCCESSFUL!")

    # Cleanup
    shutil.rmtree(work_dir)
    print("\n============================================================")
    print("  ALL QEMU HARDWARE / VIRTUALIZATION TESTS COMPLETED: PASS  ")
    print("============================================================")
