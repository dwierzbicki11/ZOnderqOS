#!/usr/bin/env python3
"""Automated idle memory measurement harness for ZonderqOS Gen 3 across scenarios A-F.
Runs real kernel in QEMU, captures COM1 serial telemetry [MEM_SAMPLE],
and evaluates heap growth against acceptance criteria.
"""
import json
import os
import shutil
import socket
import subprocess
import sys
import time
from pathlib import Path
from pyfatfs.PyFatFS import PyFatFS

ROOT = Path("/home/zonderq/Pulpit/ZOnderqOS")
ISO = ROOT / "bin/Debug/net10.0/linux-x64/cosmos/ZonderqOS.iso"
KERNEL_ELF = ROOT / "obj/Debug/net10.0/linux-x64/cosmos/iso_root/boot/ZonderqOS.elf"

def prepare_disk(disk_path):
    if disk_path.exists():
        disk_path.unlink()
    subprocess.run(["truncate", "-s", "256M", str(disk_path)], check=True)
    sfdisk_proc = subprocess.Popen(
        ["sfdisk", str(disk_path)],
        stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, text=True
    )
    sfdisk_proc.communicate("label: dos\nunit: sectors\nstart=2048, type=c\n")
    if sfdisk_proc.returncode != 0:
        raise RuntimeError("sfdisk failed")
    subprocess.run(
        ["mkfs.vfat", "-I", "-F", "32", "-n", "ZONDERQDATA", "-h", "2048", "--offset", "2048", str(disk_path)],
        check=True, stdout=subprocess.DEVNULL
    )

    fs = PyFatFS(str(disk_path), offset=2048 * 512)
    fs.makedir("/boot")
    fs.makedir("/boot/slot_a")
    fs.makedir("/boot/slot_b")
    fs.makedir("/boot/limine")
    fs.makedir("/var")
    fs.makedir("/var/log")
    fs.makedir("/updates")
    fs.makedir("/etc")
    fs.makedir("/etc/zonderq")

    settings_cfg = (
        "# ZOnderqOS Gen3 system settings\n"
        "schema=1\n"
        "performance=1\n"
        "clock_seconds=false\n"
        "taskbar_date=true\n"
        "tray_status=true\n"
        "desktop_icons=true\n"
        "boot_gui=true\n"
        "timezone=1\n"
        "desktop_background=0\n"
        "accent_theme=0\n"
        "language=pl-PL\n"
        "auto_lock_minutes=0\n"
        "network_dhcp=true\n"
        "static_ip=192.168.1.50\n"
        "static_mask=255.255.255.0\n"
        "static_gateway=192.168.1.1\n"
        "dns_server=1.1.1.1\n"
    )
    with fs.open("/etc/zonderq/settings.conf", "w") as scf:
        scf.write(settings_cfg)

    with open(KERNEL_ELF, "rb") as kf:
        kernel_bytes = kf.read()
    with fs.open("/boot/slot_a/ZonderqOS.elf", "wb") as out_kf:
        out_kf.write(kernel_bytes)
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
        "timeout: 1\n"
        "default_entry: 1\n\n"
        "/ZOnderqOS\n"
        "    protocol: limine\n"
        "    resolution: 1366x768x32\n"
        "    path: fslabel(ZONDERQDATA):/boot/slot_a/ZonderqOS.elf\n"
    )
    with fs.open("/boot/limine.conf", "w") as lcf:
        lcf.write(limine_cfg)
    with fs.open("/limine.conf", "w") as lcf:
        lcf.write(limine_cfg)
    with fs.open("/boot/limine/limine.conf", "w") as lcf:
        lcf.write(limine_cfg)
    fs.close()

def qmp_command(channel, cmd_dict):
    channel.write((json.dumps(cmd_dict) + "\r\n").encode("ascii"))
    while True:
        line = channel.readline()
        if not line:
            raise RuntimeError("QMP channel closed")
        reply = json.loads(line)
        if "error" in reply:
            raise RuntimeError("QMP error: " + str(reply["error"]))
        if "return" in reply:
            return reply["return"]

def qmp_send_keys(channel, *keys):
    qmp_command(channel, {
        "execute": "send-key",
        "arguments": {
            "keys": [{"type": "qcode", "data": k} for k in keys],
            "hold-time": 80,
        }
    })
    time.sleep(0.18)

def qmp_click_mouse(channel, button="left"):
    qmp_command(channel, {
        "execute": "input-send-event",
        "arguments": {"events": [{"type": "btn", "data": {"down": True, "button": button}}]}
    })
    time.sleep(0.08)
    qmp_command(channel, {
        "execute": "input-send-event",
        "arguments": {"events": [{"type": "btn", "data": {"down": False, "button": button}}]}
    })
    time.sleep(0.1)

def qmp_move_mouse(channel, dx, dy):
    events = []
    if dx: events.append({"type": "rel", "data": {"axis": "x", "value": dx}})
    if dy: events.append({"type": "rel", "data": {"axis": "y", "value": dy}})
    if events:
        qmp_command(channel, {"execute": "input-send-event", "arguments": {"events": events}})
        time.sleep(0.05)

def qmp_move_to_corner_then(channel, target_x, target_y, width=1366, height=768):
    dx, dy = width + 64, height + 64
    while dx or dy:
        sx, sy = min(64, dx), min(64, dy)
        qmp_move_mouse(channel, -sx, -sy)
        dx -= sx
        dy -= sy
    dx, dy = target_x, target_y
    while dx or dy:
        sx, sy = min(64, dx), min(64, dy)
        qmp_move_mouse(channel, sx, sy)
        dx -= sx
        dy -= sy

def parse_samples(serial_path):
    samples = []
    if not serial_path.exists():
        return samples
    with open(serial_path, "r", errors="ignore") as f:
        for line in f:
            if line.startswith("[MEM_SAMPLE]"):
                parts = line.strip().split()
                data = {}
                for part in parts[1:]:
                    if "=" in part:
                        k, v = part.split("=", 1)
                        try:
                            if "." in v:
                                data[k] = float(v)
                            else:
                                data[k] = int(v)
                        except ValueError:
                            data[k] = v
                if "sec" in data and "used_mb" in data:
                    samples.append(data)
    return samples

def run_scenario(scenario_name, smp=1, duration_sec=300, out_dir=None):
    if out_dir is None:
        out_dir = Path(f"/tmp/zonderq_bench_{scenario_name}_smp{smp}")
    if out_dir.exists():
        shutil.rmtree(out_dir)
    out_dir.mkdir(parents=True)

    disk_path = out_dir / "zonderqdata.img"
    prepare_disk(disk_path)

    serial_log = out_dir / "serial.log"
    qmp_sock = out_dir / "qmp.sock"

    cmd = [
        "qemu-system-x86_64",
        "-M", "q35", "-accel", "tcg", "-cpu", "Nehalem",
        "-smp", str(smp), "-m", "2048M",
        "-vga", "std",
        "-drive", f"file={ISO},if=none,id=cosmoscd,format=raw,readonly=on",
        "-device", "ide-cd,drive=cosmoscd,bootindex=0", "-boot", "d",
        "-device", "ich9-ahci,id=ahci0",
        "-drive", f"file={disk_path},if=none,id=ahcidisk0,format=raw,cache=directsync",
        "-device", "ide-hd,drive=ahcidisk0,bus=ahci0.0",
        "-display", "none",
        "-monitor", "none",
        "-serial", f"file:{serial_log}",
        "-qmp", f"unix:{qmp_sock},server=on,wait=off",
        "-no-reboot", "-no-shutdown"
    ]

    print(f"[{scenario_name} smp={smp}] Launching QEMU for {duration_sec}s...")
    proc = subprocess.Popen(cmd)
    try:
        # Wait 25s for boot splash & setup screen
        time.sleep(25)
        print(f"[{scenario_name} smp={smp}] Logging in...")
        subprocess.run(["python3", str(ROOT / "tools/qmp-gui-login.py"), str(qmp_sock), "first"],
                       check=True, capture_output=True)
        time.sleep(5) # Let desktop stabilize

        # Connect to QMP for scenario actions
        with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as conn:
            conn.settimeout(20)
            conn.connect(str(qmp_sock))
            channel = conn.makefile("rwb", buffering=0)
            json.loads(channel.readline()) # greeting
            qmp_command(channel, {"execute": "qmp_capabilities"})

            if scenario_name == "A":
                # Bare desktop: do nothing
                print(f"[{scenario_name} smp={smp}] Bare desktop scenario - idling...")
                pass

            elif scenario_name == "B":
                # Task Manager on RAM tab
                print(f"[{scenario_name} smp={smp}] Opening Task Manager...")
                qmp_send_keys(channel, "alt", "spc")
                time.sleep(1)
                for c in "task":
                    qmp_send_keys(channel, c)
                time.sleep(1)
                qmp_send_keys(channel, "ret")
                time.sleep(4)
                # Performance tab
                qmp_send_keys(channel, "right")
                time.sleep(1)
                # Memory resource
                qmp_send_keys(channel, "down")
                time.sleep(1)
                print(f"[{scenario_name} smp={smp}] Task Manager on RAM tab - idling...")

            elif scenario_name == "C":
                # Task Manager on CPU tab
                print(f"[{scenario_name} smp={smp}] Opening Task Manager on CPU tab...")
                qmp_send_keys(channel, "alt", "spc")
                time.sleep(1)
                for c in "task":
                    qmp_send_keys(channel, c)
                time.sleep(1)
                qmp_send_keys(channel, "ret")
                time.sleep(4)
                # Performance tab (CPU is default)
                qmp_send_keys(channel, "right")
                time.sleep(1)
                print(f"[{scenario_name} smp={smp}] Task Manager on CPU tab - idling...")

            elif scenario_name == "D":
                # Task Manager minimized
                print(f"[{scenario_name} smp={smp}] Opening Task Manager then minimizing...")
                qmp_send_keys(channel, "alt", "spc")
                time.sleep(1)
                for c in "task":
                    qmp_send_keys(channel, c)
                time.sleep(1)
                qmp_send_keys(channel, "ret")
                time.sleep(4)
                # Bounds on 1366x768: X=165, Y=22, Width=1040, Height=690
                # Minimize button center: X + Width - 79 = 165 + 1040 - 79 = 1126
                # Y + 19 = 22 + 19 = 41
                qmp_move_to_corner_then(channel, 1126, 41)
                qmp_click_mouse(channel)
                time.sleep(2)
                print(f"[{scenario_name} smp={smp}] Task Manager minimized - idling...")

            elif scenario_name == "E":
                # Task Manager closed after opening
                print(f"[{scenario_name} smp={smp}] Opening Task Manager then closing...")
                qmp_send_keys(channel, "alt", "spc")
                time.sleep(1)
                for c in "task":
                    qmp_send_keys(channel, c)
                time.sleep(1)
                qmp_send_keys(channel, "ret")
                time.sleep(4)
                qmp_send_keys(channel, "esc")
                time.sleep(2)
                print(f"[{scenario_name} smp={smp}] Task Manager closed - idling...")

            elif scenario_name == "F":
                # Repeated opening and closing (5 cycles)
                print(f"[{scenario_name} smp={smp}] Cycling Task Manager open/close 5 times...")
                for cycle in range(5):
                    qmp_send_keys(channel, "alt", "spc")
                    time.sleep(0.8)
                    for c in "task":
                        qmp_send_keys(channel, c)
                    time.sleep(0.8)
                    qmp_send_keys(channel, "ret")
                    time.sleep(2.5)
                    qmp_send_keys(channel, "esc")
                    time.sleep(1.5)
                print(f"[{scenario_name} smp={smp}] Cycles complete - idling...")

        # Remaining measurement time
        remaining = max(0, duration_sec - 35)
        print(f"[{scenario_name} smp={smp}] Collecting samples for remaining {remaining}s...", flush=True)
        t0 = time.time()
        last_report = t0
        while time.time() - t0 < remaining:
            if proc.poll() is not None:
                raise RuntimeError(f"QEMU exited prematurely with code {proc.returncode}")
            time.sleep(5)
            if time.time() - last_report >= 60:
                last_report = time.time()
                cur_samples = parse_samples(serial_log)
                if cur_samples:
                    s_latest = cur_samples[-1]
                    s_first = cur_samples[0]
                    elapsed = s_latest['sec'] - s_first['sec']
                    delta_mb = s_latest['used_mb'] - s_first['used_mb']
                    rate = (delta_mb / (elapsed / 60.0)) if elapsed > 0 else 0
                    print(f"[{scenario_name} smp={smp} PROGRESS {int(time.time()-t0)}s/{remaining}s] "
                          f"latest t={s_latest['sec']}s: used={s_latest['used_mb']}MB, delta={delta_mb:+.2f}MB, rate={rate:+.3f}MB/min",
                          flush=True)

    finally:
        if proc.poll() is None:
            proc.terminate()
            try:
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait()

    samples = parse_samples(serial_log)
    print(f"[{scenario_name} smp={smp}] Finished. Captured {len(samples)} samples.", flush=True)

    if samples:
        t_start = samples[0]['sec']
        t_end = samples[-1]['sec']
        total_span = t_end - t_start

        # Split warmup (first 300s from boot or first 25% of run) and steady-state
        warmup_cutoff = min(t_start + 300, t_start + total_span // 3)
        warmup_samples = [s for s in samples if s['sec'] <= warmup_cutoff]
        steady_samples = [s for s in samples if s['sec'] > warmup_cutoff]

        print(f"\n=======================================================", flush=True)
        print(f"MEASUREMENT REPORT: Scenario {scenario_name} (smp={smp})", flush=True)
        print(f"=======================================================", flush=True)
        print(f"Total test span: {total_span}s ({len(samples)} samples)", flush=True)
        print(f"Initial sample: t={samples[0]['sec']}s, used={samples[0]['used_mb']} MB ({samples[0]['used_p']} pages)", flush=True)
        print(f"Final sample:   t={samples[-1]['sec']}s, used={samples[-1]['used_mb']} MB ({samples[-1]['used_p']} pages)", flush=True)
        net_delta = samples[-1]['used_mb'] - samples[0]['used_mb']
        print(f"Total net delta: {net_delta:+.2f} MB", flush=True)

        if steady_samples:
            s_first_steady = steady_samples[0]
            s_last_steady = steady_samples[-1]
            steady_span = s_last_steady['sec'] - s_first_steady['sec']
            steady_delta = s_last_steady['used_mb'] - s_first_steady['used_mb']
            steady_rate = (steady_delta / (steady_span / 60.0)) if steady_span > 0 else 0.0
            print(f"Steady-state span: {steady_span}s ({len(steady_samples)} samples, from t={s_first_steady['sec']}s to t={s_last_steady['sec']}s)", flush=True)
            print(f"Steady-state delta: {steady_delta:+.2f} MB", flush=True)
            print(f"Steady-state growth rate: {steady_rate:+.4f} MB/min", flush=True)

            # Last 20 minutes (1200s) analysis if test was long enough
            last20_cutoff = max(s_first_steady['sec'], t_end - 1200)
            last20_samples = [s for s in steady_samples if s['sec'] >= last20_cutoff]
            if len(last20_samples) >= 2:
                last20_delta = last20_samples[-1]['used_mb'] - last20_samples[0]['used_mb']
                last20_span = last20_samples[-1]['sec'] - last20_samples[0]['sec']
                last20_rate = (last20_delta / (last20_span / 60.0)) if last20_span > 0 else 0.0
                print(f"Last period ({last20_span}s): delta={last20_delta:+.2f} MB, rate={last20_rate:+.4f} MB/min", flush=True)

            passed_16mb = steady_delta <= 16.0
            print(f"Criteria check (steady-state delta <= 16 MiB): {'PASS' if passed_16mb else 'FAIL'}", flush=True)
        print(f"=======================================================\n", flush=True)

    return samples

if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--scenario", default="B", choices=["A", "B", "C", "D", "E", "F", "ALL"])
    parser.add_argument("--smp", type=int, default=1)
    parser.add_argument("--duration", type=int, default=300)
    args = parser.parse_args()

    if args.scenario == "ALL":
        results = {}
        for sc in ["A", "B", "C", "D", "E", "F"]:
            results[sc] = run_scenario(sc, smp=args.smp, duration_sec=args.duration)
        with open("/tmp/results_summary.json", "w") as f:
            json.dump(results, f, indent=2)
    else:
        samples = run_scenario(args.scenario, smp=args.smp, duration_sec=args.duration)
        for s in samples:
            print(f"t={s['sec']}s: used={s['used_mb']} MB ({s['used_p']} pages), free={s['free_p']}")
