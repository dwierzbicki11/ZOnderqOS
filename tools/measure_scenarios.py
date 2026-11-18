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

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUTPUT_ISO = ROOT / "output-x64/ZonderqOS.iso"
DEFAULT_OUTPUT_ELF = ROOT / "output-x64/ZonderqOS.elf"
DEFAULT_DEBUG_ISO = ROOT / "bin/Debug/net10.0/linux-x64/cosmos/ZonderqOS.iso"
DEFAULT_DEBUG_ELF = ROOT / "obj/Debug/net10.0/linux-x64/cosmos/iso_root/boot/ZonderqOS.elf"
DEFAULT_RELEASE_ISO = ROOT / "bin/Release/net10.0/linux-x64/cosmos/ZonderqOS.iso"
DEFAULT_RELEASE_ELF = ROOT / "obj/Release/net10.0/linux-x64/cosmos/iso_root/boot/ZonderqOS.elf"

ISO = DEFAULT_OUTPUT_ISO if DEFAULT_OUTPUT_ISO.exists() else (
    DEFAULT_DEBUG_ISO if DEFAULT_DEBUG_ISO.exists() else DEFAULT_RELEASE_ISO
)
KERNEL_ELF = DEFAULT_OUTPUT_ELF if DEFAULT_OUTPUT_ELF.exists() else (
    DEFAULT_DEBUG_ELF if DEFAULT_DEBUG_ELF.exists() else DEFAULT_RELEASE_ELF
)


def prepare_disk(disk_path, kernel_elf=None):
    elf_path = Path(kernel_elf) if kernel_elf else KERNEL_ELF
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

    with open(elf_path, "rb") as kf:
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

def extract_60s_checkpoints(samples):
    if not samples:
        return []
    checkpoints = [samples[0]]
    next_target = samples[0]["sec"] + 60
    for s in samples[1:]:
        if s["sec"] >= next_target:
            checkpoints.append(s)
            while next_target <= s["sec"]:
                next_target += 60
    if checkpoints[-1]["sec"] != samples[-1]["sec"]:
        checkpoints.append(samples[-1])
    return checkpoints


def open_task_manager(channel):
    qmp_send_keys(channel, "ctrl", "shift", "esc")
    time.sleep(2.5)


def setup_workload(channel, workload, cycles=8):
    # Dismiss startup WelcomeApp so bare desktop starts with zero open apps.
    qmp_send_keys(channel, "esc")
    time.sleep(1.2)

    if workload == "A":
        pass
    elif workload == "B":
        open_task_manager(channel)
        # Wydajnosc -> Pamiec (RAM)
        qmp_send_keys(channel, "right")
        time.sleep(0.8)
        qmp_send_keys(channel, "down")
        time.sleep(0.8)
    elif workload == "C":
        open_task_manager(channel)
        # Wydajnosc -> System
        qmp_send_keys(channel, "right")
        time.sleep(0.8)
        qmp_send_keys(channel, "down")
        time.sleep(0.6)
        qmp_send_keys(channel, "down")
        time.sleep(0.8)
    elif workload == "D":
        for _ in range(cycles):
            open_task_manager(channel)
            qmp_send_keys(channel, "esc")
            time.sleep(1.5)
    elif workload == "E":
        # Lock screen (Ctrl+Alt+L)
        qmp_send_keys(channel, "ctrl", "alt", "l")
        time.sleep(2.0)


def run_scenario(scenario_name, smp=1, duration_sec=300, out_dir=None, iso_path=None, kernel_elf=None, cycles=8, workload=None, ram="2048M"):
    iso = Path(iso_path) if iso_path else ISO
    elf = Path(kernel_elf) if kernel_elf else KERNEL_ELF
    if not iso.exists():
        raise FileNotFoundError(f"ISO not found: {iso}")
    if not elf.exists():
        raise FileNotFoundError(f"Kernel ELF not found: {elf}")

    active_workload = workload or ("B" if scenario_name.startswith("F") else scenario_name)
    if out_dir is None:
        out_dir = Path(f"/tmp/zonderq_bench_{scenario_name}_smp{smp}")
    else:
        out_dir = Path(out_dir)
    if out_dir.exists():
        shutil.rmtree(out_dir)
    out_dir.mkdir(parents=True)

    disk_path = out_dir / "zonderqdata.img"
    prepare_disk(disk_path, kernel_elf=elf)

    serial_log = out_dir / "serial.log"
    qmp_sock = Path(f"/tmp/zonderq_qmp_{os.getpid()}_{smp}.sock")
    if qmp_sock.exists():
        qmp_sock.unlink()
    screendump_path = out_dir / "setup_check.ppm"

    cmd = [
        "qemu-system-x86_64",
        "-M", "q35", "-accel", "tcg", "-cpu", "Nehalem",
        "-smp", str(smp), "-m", ram,
        "-vga", "std",
        "-drive", f"file={iso},if=none,id=cosmoscd,format=raw,readonly=on",
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

    print(f"[{scenario_name} smp={smp}] Launching QEMU ({iso.name}) for {duration_sec}s...")
    proc = subprocess.Popen(cmd)
    try:
        # Wait until Kernel.Run() enters InitialSetupManager (immune to TCG speed variance)
        t_boot = time.time()
        while time.time() - t_boot < 45:
            if proc.poll() is not None:
                raise RuntimeError(f"QEMU exited during boot wait with code {proc.returncode}")
            if serial_log.exists() and "[Kernel] Calling Run()" in serial_log.read_text(errors="ignore"):
                break
            time.sleep(0.5)
        time.sleep(3)

        print(f"[{scenario_name} smp={smp}] Logging in...")
        subprocess.run(["python3", str(ROOT / "tools/qmp-gui-login.py"), str(qmp_sock), "first"],
                       check=True, capture_output=True)

        # Wait until GuiManager completes wallpaper/icon decode and enters active desktop loop
        t_desk = time.time()
        while time.time() - t_desk < 45:
            if proc.poll() is not None:
                raise RuntimeError(f"QEMU exited while loading desktop with code {proc.returncode}")
            if parse_samples(serial_log):
                break
            time.sleep(0.5)
        time.sleep(1.5)  # Let WelcomeApp finish initial frame

        # Connect to QMP for scenario actions
        with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as conn:
            conn.settimeout(20)
            conn.connect(str(qmp_sock))
            channel = conn.makefile("rwb", buffering=0)
            json.loads(channel.readline())  # greeting
            qmp_command(channel, {"execute": "qmp_capabilities"})

            print(f"[{scenario_name} smp={smp}] Setting up scenario workload={active_workload}...", flush=True)
            setup_workload(channel, active_workload, cycles=cycles)
            time.sleep(1.5)
            try:
                qmp_command(channel, {"execute": "screendump", "arguments": {"filename": str(screendump_path)}})
            except Exception as ex:
                print(f"[{scenario_name} smp={smp}] screendump warning: {ex}", flush=True)

        # Wait comparable measurement span after login/setup
        setup_samples = parse_samples(serial_log)
        measure_start_sec = setup_samples[-1]["sec"] if setup_samples else None
        t0 = time.time()
        last_report = t0
        while time.time() - t0 < duration_sec:
            if proc.poll() is not None:
                raise RuntimeError(f"QEMU exited prematurely with code {proc.returncode}")
            time.sleep(5)
            if time.time() - last_report >= 60:
                last_report = time.time()
                cur_samples = parse_samples(serial_log)
                if cur_samples:
                    s_first = next((s for s in cur_samples if measure_start_sec is None or s["sec"] >= measure_start_sec), cur_samples[0])
                    s_latest = cur_samples[-1]
                    elapsed = s_latest["sec"] - s_first["sec"]
                    delta_mb = s_latest["used_mb"] - s_first["used_mb"]
                    rate = (delta_mb / (elapsed / 60.0)) if elapsed > 0 else 0.0
                    print(f"[{scenario_name} smp={smp} PROGRESS {int(time.time()-t0)}s/{duration_sec}s] "
                          f"t={s_latest['sec']}s: used={s_latest['used_mb']}MB (delta={delta_mb:+.2f}MB, rate={rate:+.3f}MB/min) "
                          f"gc_comm={s_latest.get('gc_comm_kb')}KB gc_heap={s_latest.get('gc_heap_kb')}KB threads={s_latest.get('threads', '?')}",
                          flush=True)

    finally:
        if proc.poll() is None:
            proc.terminate()
            try:
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait()
        qmp_sock.unlink(missing_ok=True)

    samples = parse_samples(serial_log)
    checkpoints = extract_60s_checkpoints(samples)
    print(f"[{scenario_name} smp={smp}] Finished. Captured {len(samples)} samples ({len(checkpoints)} checkpoints).", flush=True)

    summary = {
        "scenario": scenario_name,
        "workload": active_workload,
        "smp": smp,
        "iso": str(iso),
        "kernel_elf": str(elf),
        "sample_count": len(samples),
        "checkpoints_60s": checkpoints,
    }

    if samples:
        t_start = samples[0]["sec"]
        t_end = samples[-1]["sec"]
        total_span = t_end - t_start
        warmup_cutoff = min(t_start + 120, t_start + total_span // 3)
        steady_samples = [s for s in samples if s["sec"] > warmup_cutoff]

        s0 = samples[0]
        s1 = samples[-1]
        net_pages = s1["used_p"] - s0["used_p"]
        net_bytes = net_pages * 4096
        net_mb = s1["used_mb"] - s0["used_mb"]
        gc_comm_delta_kb = s1.get("gc_comm_kb", 0) - s0.get("gc_comm_kb", 0)
        gc_seg_delta = gc_comm_delta_kb / 64.0

        summary.update({
            "t_start": t_start,
            "t_end": t_end,
            "span_sec": total_span,
            "initial": s0,
            "final": s1,
            "net_pages": net_pages,
            "net_bytes": net_bytes,
            "net_mb": round(net_mb, 3),
            "gc_comm_delta_kb": gc_comm_delta_kb,
            "gc_seg_delta": round(gc_seg_delta, 3),
        })

        print(f"\n=======================================================", flush=True)
        print(f"MEASUREMENT REPORT: Scenario {scenario_name} (workload={active_workload}, smp={smp})", flush=True)
        print(f"=======================================================", flush=True)
        print(f"Total span: {total_span}s ({len(samples)} samples)", flush=True)
        print(f"Initial: t={s0['sec']}s, used={s0['used_mb']} MB ({s0['used_p']} pages), p_gc={s0.get('p_gc')}, p_rat={s0.get('p_rat')}, p_sys={s0.get('p_sys')}, gc_comm={s0.get('gc_comm_kb')}KB, gc_heap={s0.get('gc_heap_kb')}KB, gc_frag={s0.get('gc_frag_kb')}KB, gc_gen={s0.get('gc_gen')}, threads={s0.get('threads', 'N/A')}", flush=True)
        print(f"Final:   t={s1['sec']}s, used={s1['used_mb']} MB ({s1['used_p']} pages), p_gc={s1.get('p_gc')}, p_rat={s1.get('p_rat')}, p_sys={s1.get('p_sys')}, gc_comm={s1.get('gc_comm_kb')}KB, gc_heap={s1.get('gc_heap_kb')}KB, gc_frag={s1.get('gc_frag_kb')}KB, gc_gen={s1.get('gc_gen')}, threads={s1.get('threads', 'N/A')}", flush=True)
        print(f"Net delta: {net_mb:+.2f} MB ({net_pages:+d} pages, {net_bytes:+d} bytes, GC comm {gc_comm_delta_kb:+d} KB / {gc_seg_delta:+.2f} segments)", flush=True)

        if steady_samples:
            s_first_steady = steady_samples[0]
            s_last_steady = steady_samples[-1]
            steady_span = s_last_steady["sec"] - s_first_steady["sec"]
            steady_delta = s_last_steady["used_mb"] - s_first_steady["used_mb"]
            steady_pages = s_last_steady["used_p"] - s_first_steady["used_p"]
            steady_rate = (steady_delta / (steady_span / 60.0)) if steady_span > 0 else 0.0
            summary.update({
                "steady_start": s_first_steady,
                "steady_end": s_last_steady,
                "steady_span": steady_span,
                "steady_delta_mb": round(steady_delta, 3),
                "steady_pages": steady_pages,
                "steady_rate_mb_min": round(steady_rate, 4),
            })
            print(f"Steady-state ({s_first_steady['sec']}s -> {s_last_steady['sec']}s, {steady_span}s): {steady_delta:+.2f} MB ({steady_pages:+d} pages, {steady_rate:+.4f} MB/min)", flush=True)

        print("60s Checkpoints:", flush=True)
        for cp in checkpoints:
            print(
                f"  t={cp['sec']:>4}s | used={cp['used_mb']:>6.2f}MB ({cp['used_p']}p) | "
                f"p_gc={cp.get('p_gc')} p_rat={cp.get('p_rat')} p_sys={cp.get('p_sys')} | "
                f"gc_comm={cp.get('gc_comm_kb')}KB gc_heap={cp.get('gc_heap_kb')}KB "
                f"gc_frag={cp.get('gc_frag_kb')}KB gc_gen={cp.get('gc_gen')} threads={cp.get('threads', 'N/A')}",
                flush=True
            )
        print(f"=======================================================\n", flush=True)

    with open(out_dir / "summary.json", "w") as sf:
        json.dump(summary, sf, indent=2)
    return summary


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--scenario", default="B", choices=["A", "B", "C", "D", "E", "F", "ALL"])
    parser.add_argument("--smp", type=int, default=None)
    parser.add_argument("--ram", default="2048M")
    parser.add_argument("--duration", type=int, default=300)
    parser.add_argument("--cycles", type=int, default=8)
    parser.add_argument("--workload", default=None, choices=["A", "B", "C", "D", "E"])
    parser.add_argument("--iso", default=None)
    parser.add_argument("--kernel", default=None)
    parser.add_argument("--out-dir", default=None)
    args = parser.parse_args()

    if args.scenario == "ALL":
        base_out = Path(args.out_dir) if args.out_dir else Path("/tmp/zonderq_bench_suite")
        base_out.mkdir(parents=True, exist_ok=True)
        suite = {}
        default_smp = args.smp if args.smp is not None else 1
        runs = [
            ("A", default_smp, None),
            ("B", default_smp, None),
            ("C", default_smp, None),
            ("D", default_smp, None),
            ("E", default_smp, None),
            ("F_1vcpu", 1, args.workload or "B"),
            ("F_8vcpu", 8, args.workload or "B"),
        ]
        for name, smp_val, wl in runs:
            suite[name] = run_scenario(
                name,
                smp=smp_val,
                duration_sec=args.duration,
                out_dir=base_out / f"{name}_smp{smp_val}",
                iso_path=args.iso,
                kernel_elf=args.kernel,
                cycles=args.cycles,
                workload=wl,
                ram=args.ram,
            )
        with open(base_out / "results_summary.json", "w") as f:
            json.dump(suite, f, indent=2)
    elif args.scenario == "F" and args.smp is None:
        base_out = Path(args.out_dir) if args.out_dir else Path("/tmp/zonderq_bench_F")
        base_out.mkdir(parents=True, exist_ok=True)
        suite = {}
        for smp_val in (1, 8):
            name = f"F_{smp_val}vcpu"
            suite[name] = run_scenario(
                name,
                smp=smp_val,
                duration_sec=args.duration,
                out_dir=base_out / name,
                iso_path=args.iso,
                kernel_elf=args.kernel,
                cycles=args.cycles,
                workload=args.workload or "B",
                ram=args.ram,
            )
        with open(base_out / "results_summary.json", "w") as f:
            json.dump(suite, f, indent=2)
    else:
        smp_val = args.smp if args.smp is not None else 1
        run_scenario(
            args.scenario,
            smp=smp_val,
            duration_sec=args.duration,
            out_dir=args.out_dir,
            iso_path=args.iso,
            kernel_elf=args.kernel,
            cycles=args.cycles,
            workload=args.workload,
            ram=args.ram,
        )
