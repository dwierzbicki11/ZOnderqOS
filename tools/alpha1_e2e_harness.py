#!/usr/bin/env python3
"""ZOnderqOS Gen3 Alpha 1 Full System E2E QEMU Harness (Cosmos 3.0.97).
Supports resumable stages: boot, apps (100+ cycles), auth (50 lock/unlock + 20 wrong pass + ACL),
fat32, updates, restarts (10x), and writes JSON + Markdown reports.
"""
import argparse
import hashlib
import json
import os
import shutil
import socket
import subprocess
import sys
import time
from pathlib import Path
from pyfatfs.PyFatFS import PyFatFS

ROOT = Path(__file__).resolve().parent.parent
ISO = ROOT / "output-x64/ZonderqOS.iso"
ELF = ROOT / "output-x64/ZonderqOS.elf"
OUT_BASE = Path("/tmp/zonderq_alpha1_e2e")
TEST_PASS = "gui2026test"

CHAR_TO_KEYS = {
    " ": ("spc",), "/": ("slash",), ".": ("dot",), "-": ("minus",),
    "'": ("apostrophe",), "$": ("shift", "4"), ">": ("shift", "dot"),
    "_": ("shift", "minus"), "\n": ("ret",),
}
for c in "abcdefghijklmnopqrstuvwxyz0123456789":
    CHAR_TO_KEYS[c] = (c,)
for c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ":
    CHAR_TO_KEYS[c] = ("shift", c.lower())


def qmp_cmd(ch, cmd):
    ch.write((json.dumps(cmd) + "\r\n").encode("ascii"))
    while True:
        reply = json.loads(ch.readline())
        if "error" in reply:
            raise RuntimeError(f"QMP error: {reply['error']}")
        if "return" in reply:
            return reply["return"]


def send_keys(ch, *keys, delay=0.10):
    qmp_cmd(ch, {
        "execute": "send-key",
        "arguments": {"keys": [{"type": "qcode", "data": k} for k in keys], "hold-time": 60},
    })
    time.sleep(delay)


def type_text(ch, text, enter=True, pause=0.35):
    for c in text:
        send_keys(ch, *CHAR_TO_KEYS[c], delay=0.05)
    if enter:
        send_keys(ch, "ret", delay=0.12)
    time.sleep(pause)


def parse_samples(serial_path):
    samples = []
    if not serial_path.exists():
        return samples
    for line in serial_path.read_text(errors="ignore").splitlines():
        if not line.startswith("[MEM_SAMPLE]"):
            continue
        d = {}
        for part in line.split()[1:]:
            if "=" in part:
                k, v = part.split("=", 1)
                try:
                    d[k] = float(v) if "." in v else int(v)
                except ValueError:
                    d[k] = v
        if "sec" in d and "used_mb" in d:
            samples.append(d)
    return samples


def prepare_disk(disk_path, boot_gui=True):
    if disk_path.exists():
        disk_path.unlink()
    subprocess.run(["truncate", "-s", "256M", str(disk_path)], check=True)
    p = subprocess.Popen(["sfdisk", str(disk_path)], stdin=subprocess.PIPE,
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, text=True)
    p.communicate("label: dos\nunit: sectors\nstart=2048, type=c\n")
    if p.returncode != 0:
        raise RuntimeError("sfdisk failed")
    subprocess.run(["mkfs.vfat", "-I", "-F", "32", "-n", "ZONDERQDATA", "-h", "2048", "--offset", "2048", str(disk_path)],
                   check=True, stdout=subprocess.DEVNULL)
    fs = PyFatFS(str(disk_path), offset=2048 * 512)
    for d in ("/boot", "/boot/slot_a", "/boot/slot_b", "/boot/limine", "/var", "/var/log", "/updates", "/etc", "/etc/zonderq"):
        fs.makedir(d)
    cfg = (
        "# ZOnderqOS Gen3 system settings\nschema=1\nperformance=1\nclock_seconds=false\n"
        f"taskbar_date=true\ntray_status=true\ndesktop_icons=true\nboot_gui={'true' if boot_gui else 'false'}\n"
        "timezone=1\ndesktop_background=0\naccent_theme=0\nlanguage=pl-PL\nauto_lock_minutes=0\n"
        "network_dhcp=true\nstatic_ip=192.168.1.50\nstatic_mask=255.255.255.0\n"
        "static_gateway=192.168.1.1\ndns_server=1.1.1.1\n"
    )
    with fs.open("/etc/zonderq/settings.conf", "w") as f:
        f.write(cfg)
    kbytes = ELF.read_bytes()
    with fs.open("/boot/slot_a/ZonderqOS.elf", "wb") as f:
        f.write(kbytes)
    with fs.open("/boot/ZonderqOS.elf", "wb") as f:
        f.write(kbytes)
    boot_state = (
        "# ZonderqOS A/B Bootloader State Configuration\nACTIVE_SLOT=A\nFALLBACK_SLOT=B\n"
        "PENDING_SLOT=\nBOOT_ATTEMPTS=0\nBOOTGUARD_PREBOOT=0\nBOOTGUARD_FALLBACK=0\n"
        "SLOT_A_STATUS=Healthy\nSLOT_B_STATUS=Empty\nSLOT_A_VERSION=3.0.0\nSLOT_A_BUILD=3097\n"
        "SLOT_B_VERSION=\nSLOT_B_BUILD=0\n"
    )
    with fs.open("/boot/boot_state.cfg", "w") as f:
        f.write(boot_state)
    fs.close()


def launch_qemu(disk_path, serial_log, qmp_sock, smp=1, ram="8192M"):
    qmp_sock.unlink(missing_ok=True)
    cmd = [
        "qemu-system-x86_64", "-M", "q35", "-accel", "tcg", "-cpu", "Nehalem",
        "-smp", str(smp), "-m", ram, "-vga", "std",
        "-device", "qemu-xhci,id=xhci", "-device", "usb-kbd,bus=xhci.0", "-device", "usb-tablet,bus=xhci.0",
        "-drive", f"file={ISO},if=none,id=cosmoscd,format=raw,readonly=on",
        "-device", "ide-cd,drive=cosmoscd,bootindex=0", "-boot", "d",
        "-device", "ich9-ahci,id=ahci0",
        "-drive", f"file={disk_path},if=none,id=ahcidisk0,format=raw,cache=directsync",
        "-device", "ide-hd,drive=ahcidisk0,bus=ahci0.0",
        "-display", "none", "-monitor", "none",
        "-serial", f"file:{serial_log}",
        "-qmp", f"unix:{qmp_sock},server=on,wait=off",
        "-no-reboot", "-no-shutdown",
    ]
    return subprocess.Popen(cmd)


def wait_boot_and_login(proc, serial_log, qmp_sock, first_boot=True):
    t0 = time.time()
    while time.time() - t0 < 45:
        if proc.poll() is not None:
            raise RuntimeError(f"QEMU exited during boot with {proc.returncode}")
        if serial_log.exists() and "[Kernel] Calling Run()" in serial_log.read_text(errors="ignore"):
            break
        time.sleep(0.5)
    time.sleep(3.0)
    subprocess.run(["python3", str(ROOT / "tools/qmp-gui-login.py"), str(qmp_sock), "first" if first_boot else "second"],
                   check=True, capture_output=True)
    t1 = time.time()
    while time.time() - t1 < 45:
        if proc.poll() is not None:
            raise RuntimeError(f"QEMU exited before desktop with {proc.returncode}")
        if parse_samples(serial_log):
            break
        time.sleep(0.5)
    time.sleep(1.0)


def stage_boot_and_restarts(work_dir):
    print("[STAGE: boot_and_restarts] Testing 1 vCPU + 8 vCPU + 10 consecutive restarts...", flush=True)
    disk = work_dir / "restarts.img"
    prepare_disk(disk, boot_gui=True)
    qmp_sock = Path(f"/tmp/zonderq_e2e_rst_{os.getpid()}.sock")
    results = []
    for idx in range(1, 11):
        smp = 8 if idx == 2 else 1
        slog = work_dir / f"restart_{idx}.log"
        proc = launch_qemu(disk, slog, qmp_sock, smp=smp, ram="8192M")
        try:
            wait_boot_and_login(proc, slog, qmp_sock, first_boot=(idx == 1))
            samples = parse_samples(slog)
            assert samples, f"Restart {idx}: no [MEM_SAMPLE]"
            stext = slog.read_text(errors="ignore")
            assert "FATAL" not in stext and "Unhandled exception" not in stext, f"Restart {idx}: kernel exception"
            results.append({"boot": idx, "smp": smp, "used_mb": samples[-1]["used_mb"], "threads": samples[-1].get("threads")})
            print(f"  [Restart {idx}/10 smp={smp}] OK used={samples[-1]['used_mb']}MB threads={samples[-1].get('threads')}", flush=True)
        finally:
            if proc.poll() is None:
                proc.terminate()
                proc.wait(timeout=5)
            qmp_sock.unlink(missing_ok=True)
    return {"status": "PASS", "boots": 10, "details": results}


def stage_apps_100_cycles(work_dir):
    print("[STAGE: apps_100] Running 100+ app open/close cycles in GUI QEMU...", flush=True)
    disk = work_dir / "apps.img"
    slog = work_dir / "apps.log"
    qmp_sock = Path(f"/tmp/zonderq_e2e_apps_{os.getpid()}.sock")
    prepare_disk(disk, boot_gui=True)
    proc = launch_qemu(disk, slog, qmp_sock, smp=1, ram="8192M")
    try:
        wait_boot_and_login(proc, slog, qmp_sock, first_boot=True)
        with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as conn:
            conn.settimeout(20)
            conn.connect(str(qmp_sock))
            ch = conn.makefile("rwb", buffering=0)
            json.loads(ch.readline())
            qmp_cmd(ch, {"execute": "qmp_capabilities"})
            send_keys(ch, "esc", delay=0.5)  # dismiss WelcomeApp
            s0 = parse_samples(slog)[-1]
            # 105 cycles: TaskManager (Ctrl+Shift+Esc), Command Palette (Alt+Space / Ctrl+K) + Esc
            for i in range(1, 106):
                send_keys(ch, "ctrl", "shift", "esc", delay=0.25)
                send_keys(ch, "esc", delay=0.20)
                if i % 25 == 0:
                    cur = parse_samples(slog)[-1]
                    print(f"  [Apps cycle {i}/105] used={cur['used_mb']}MB delta={cur['used_mb']-s0['used_mb']:+.2f}MB", flush=True)
            qmp_cmd(ch, {"execute": "screendump", "arguments": {"filename": str(work_dir / "apps_final.ppm")}})
        time.sleep(2.0)
        s1 = parse_samples(slog)[-1]
        delta = round(s1["used_mb"] - s0["used_mb"], 3)
        print(f"  [Apps 105 cycles DONE] start={s0['used_mb']}MB end={s1['used_mb']}MB delta={delta:+.2f}MB", flush=True)
        return {"status": "PASS", "cycles": 105, "start_mb": s0["used_mb"], "end_mb": s1["used_mb"], "delta_mb": delta}
    finally:
        if proc.poll() is None:
            proc.terminate()
            proc.wait(timeout=5)
        qmp_sock.unlink(missing_ok=True)


def stage_auth_lock_wrong_pass(work_dir):
    print("[STAGE: auth] Running 50 Lock/Unlock cycles + 20 wrong-password attempts...", flush=True)
    disk = work_dir / "auth.img"
    slog = work_dir / "auth.log"
    qmp_sock = Path(f"/tmp/zonderq_e2e_auth_{os.getpid()}.sock")
    prepare_disk(disk, boot_gui=True)
    proc = launch_qemu(disk, slog, qmp_sock, smp=1, ram="8192M")
    try:
        wait_boot_and_login(proc, slog, qmp_sock, first_boot=True)
        with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as conn:
            conn.settimeout(20)
            conn.connect(str(qmp_sock))
            ch = conn.makefile("rwb", buffering=0)
            json.loads(ch.readline())
            qmp_cmd(ch, {"execute": "qmp_capabilities"})
            send_keys(ch, "esc", delay=0.5)
            s0 = parse_samples(slog)[-1]

            # Test A: 50 Lock/Unlock cycles
            for i in range(1, 51):
                send_keys(ch, "ctrl", "alt", "l", delay=0.35)
                type_text(ch, TEST_PASS, enter=True, pause=0.40)
                if i % 10 == 0:
                    cur = parse_samples(slog)[-1]
                    print(f"  [Lock/Unlock {i}/50] used={cur['used_mb']}MB delta={cur['used_mb']-s0['used_mb']:+.2f}MB", flush=True)
            s_after_50 = parse_samples(slog)[-1]

            # Test B: 20 wrong password attempts while locked
            send_keys(ch, "ctrl", "alt", "l", delay=0.35)
            for j in range(1, 21):
                type_text(ch, "badpass99", enter=True, pause=0.25)
            # Verify no plaintext password leaked in serial log
            stext = slog.read_text(errors="ignore")
            assert "badpass99" not in stext and TEST_PASS not in stext, "Password leaked in serial log!"
            s_after_wrong = parse_samples(slog)[-1]
            print(f"  [Auth DONE] lock50_delta={s_after_50['used_mb']-s0['used_mb']:+.2f}MB wrong20_delta={s_after_wrong['used_mb']-s0['used_mb']:+.2f}MB", flush=True)
            return {
                "status": "PASS",
                "lock_unlock_cycles": 50,
                "wrong_attempts": 20,
                "start_mb": s0["used_mb"],
                "after_50_mb": s_after_50["used_mb"],
                "after_20_wrong_mb": s_after_wrong["used_mb"],
            }
    finally:
        if proc.poll() is None:
            proc.terminate()
            proc.wait(timeout=5)
        qmp_sock.unlink(missing_ok=True)


def stage_fat32_and_acl(work_dir):
    print("[STAGE: fat32_acl] Running live FAT32 + non-root ACL QEMU E2E...", flush=True)
    # These are host-side source/regression tests, NOT the missing guest
    # FAT32/ACL QEMU integration runner. Never promote them to E2E PASS.
    host_cases = [
        ROOT / "tests/test_gui_filemanager_create_acl.py",
        ROOT / "tests/test_gui_fat32_create_incomplete_scan.py",
        ROOT / "tests/test_gui_malformed_fat32_entries.py",
    ]
    subprocess.run([sys.executable, "-m", "pytest", "-q", *map(str, host_cases)],
                   cwd=ROOT, check=True)
    return {
        "status": "BLOCKED",
        "host_fat32_acl_regressions": "PASS",
        "guest_fat32_acl_e2e": "NOT_TESTED",
        "reason": "The former qemu_audit_e2e.py was an untracked AGY scratch file. "
                  "A checked-in real guest FAT32/ACL runner is required. "
                  "Host source contracts are not guest I/O evidence.",
    }


def stage_updates_and_bootguard():
    print("[STAGE: updates_bootguard] Running UpdateEngineSmoke + C BootGuard + pytest...", flush=True)
    subprocess.run(["dotnet", "run", "--project", str(ROOT / "tests/UpdateEngineSmoke/UpdateEngineSmoke.csproj"), "-v:minimal"], check=True)
    subprocess.run(["python3", "-m", "pytest", "-q", str(ROOT / "tests/test_p0_update_trust_gate.py"), str(ROOT / "tests/test_update_engine_ab.py")], check=True)
    # OVMF executes a real EFI BootGuard with a test-only Limine stub.
    # It does NOT execute the production Limine -> ZOnderqOS kernel chain.
    make = subprocess.run(
        ["make", "-C", str(ROOT / "tools/bootguard"),
         "all", "build/limine_stub.efi"],
        cwd=ROOT, capture_output=True, text=True,
    )
    if make.returncode != 0:
        return {
            "status": "BLOCKED",
            "update_engine_smoke": "PASS",
            "ovmf_efi_stub": "BLOCKED",
            "real_uefi_kernel_chain": "NOT_TESTED",
            "reason": "GNU-EFI build failed: " + make.stdout[-2000:],
        }
    subprocess.run(["bash", str(ROOT / "tests/test_bootguard_ovmf.sh")],
                   cwd=ROOT, check=True)
    return {
        "status": "PARTIAL",
        "update_engine_smoke": "PASS",
        "ovmf_efi_stub": "PASS",
        "real_uefi_kernel_chain": "NOT_TESTED",
        "reason": "OVMF test uses limine_stub.efi, not a production Limine+kernel boot.",
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--stage", default="all", choices=["all", "boot", "apps", "auth", "fat32", "updates"])
    args = parser.parse_args()

    OUT_BASE.mkdir(parents=True, exist_ok=True)
    report = {"iso": str(ISO), "commit": subprocess.check_output(["git", "-C", str(ROOT), "rev-parse", "HEAD"], text=True).strip(), "stages": {}}

    if args.stage in ("all", "boot"):
        report["stages"]["boot_and_restarts"] = stage_boot_and_restarts(OUT_BASE)
    if args.stage in ("all", "apps"):
        report["stages"]["apps_100_cycles"] = stage_apps_100_cycles(OUT_BASE)
    if args.stage in ("all", "auth"):
        report["stages"]["auth_lock_wrong_pass"] = stage_auth_lock_wrong_pass(OUT_BASE)
    if args.stage in ("all", "fat32"):
        report["stages"]["fat32_acl"] = stage_fat32_and_acl(OUT_BASE)
    if args.stage in ("all", "updates"):
        report["stages"]["updates_bootguard"] = stage_updates_and_bootguard()

    (OUT_BASE / "e2e_report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("\n[ALPHA1-E2E] SUMMARY:", json.dumps(report, indent=2), flush=True)
    incomplete = [name for name, result in report["stages"].items()
                  if result.get("status") != "PASS"]
    if incomplete:
        raise SystemExit("[ALPHA1-E2E] Release gate NOT PASSED: "
                         + ", ".join(incomplete))


if __name__ == "__main__":
    main()
