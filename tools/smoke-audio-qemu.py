#!/usr/bin/env python3
"""Boot a diskless Cosmos 3.0.95 audio ISO, inject 'tone' via QMP and verify WAV.

Unlike host/static gates, a PASS here requires actual non-silent samples
emitted by QEMU's HDA backend. No host disk is attached to the guest.
"""
import argparse
import json
from pathlib import Path
import socket
import subprocess
import sys
import time

from importlib.util import module_from_spec, spec_from_file_location

ROOT = Path(__file__).resolve().parents[1]
spec = spec_from_file_location("audio_capture", ROOT / "tools/verify-audio-wav-capture.py")
capture = module_from_spec(spec)
spec.loader.exec_module(capture)


class Qmp:
    def __init__(self, path):
        self.socket = socket.socket(socket.AF_UNIX)
        self.socket.settimeout(12)
        self.socket.connect(str(path))
        self.stream = self.socket.makefile("rwb", buffering=0)
        greeting = self.read()
        if "QMP" not in greeting:
            raise RuntimeError("QMP greeting missing")
        self.execute("qmp_capabilities")

    def read(self):
        line = self.stream.readline()
        if not line:
            raise RuntimeError("QMP disconnected")
        return json.loads(line)

    def execute(self, name, arguments=None):
        request = {"execute": name, "id": name}
        if arguments is not None:
            request["arguments"] = arguments
        self.stream.write((json.dumps(request) + "\r\n").encode("ascii"))
        while True:
            response = self.read()
            if response.get("id") == name:
                if "error" in response:
                    raise RuntimeError(f"QMP {name}: {response['error']}")
                return response.get("return")

    def type_line(self, line):
        for character in line + "\n":
            key = "ret" if character == "\n" else character
            for down in (True, False):
                self.execute("input-send-event", {"events": [
                    {"type": "key", "data": {"down": down, "key": {"type": "qcode", "data": key}}}
                ]})
            time.sleep(0.07)

    def close(self):
        self.stream.close()
        self.socket.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("iso", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--boot-seconds", type=int, default=35)
    args = parser.parse_args()
    if args.boot_seconds < 5 or args.boot_seconds > 120:
        parser.error("--boot-seconds must be 5..120")
    if not args.iso.is_file() or args.iso.stat().st_size == 0:
        parser.error("ISO missing or empty")

    args.output.mkdir(parents=True, exist_ok=True)
    out = args.output.resolve()
    iso = args.iso.resolve()
    qmp_socket = out / "audio.qmp"
    wav = out / "captured.wav"
    for file in (qmp_socket, wav):
        if file.exists():
            file.unlink()

    cmd = [
        "qemu-system-x86_64", "-M", "q35", "-accel", "tcg",
        "-cpu", "Nehalem", "-smp", "1", "-m", "2048M",
        "-drive", f"file={iso},if=none,id=cosmoscd,format=raw,readonly=on",
        "-device", "ide-cd,drive=cosmoscd,bootindex=0", "-boot", "d",
        "-nic", "none", "-display", "none", "-monitor", "none",
        "-serial", f"file:{out / 'serial.log'}",
        "-qmp", f"unix:{qmp_socket},server=on,wait=off",
        "-audiodev", f"wav,id=audio0,path={wav}",
        "-device", "intel-hda", "-device", "hda-duplex,audiodev=audio0",
        "-no-reboot", "-no-shutdown",
    ]
    with (out / "qemu.stderr.log").open("w") as stderr:
        guest = subprocess.Popen(cmd, stderr=stderr)
        qmp = None
        try:
            for _ in range(args.boot_seconds * 10):
                if guest.poll() is not None:
                    raise RuntimeError(f"QEMU exited early: {guest.returncode}")
                time.sleep(0.1)
            qmp = Qmp(qmp_socket)
            try:
                qmp.execute("screendump", {"filename": str(out / "before.ppm")})
            except RuntimeError as exc:
                print(f"[AUDIO-QEMU] optional screenshot unavailable: {exc}")
            qmp.type_line("audioinfo")
            time.sleep(1)
            qmp.type_line("tone")
            time.sleep(4)
            try:
                qmp.execute("screendump", {"filename": str(out / "after.ppm")})
            except RuntimeError as exc:
                print(f"[AUDIO-QEMU] optional screenshot unavailable: {exc}")
            qmp.execute("quit")
            guest.wait(timeout=15)
            if guest.returncode != 0:
                raise RuntimeError(f"QEMU quit with status {guest.returncode}")
            print(capture.inspect(wav))
            print("[AUDIO-QEMU] PASS real QEMU HDA WAV capture; no physical speaker proof")
            return 0
        except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as exc:
            print(f"[AUDIO-QEMU] FAIL {exc}", file=sys.stderr)
            return 1
        finally:
            if qmp is not None:
                qmp.close()
            if guest.poll() is None:
                guest.kill()
                guest.wait(timeout=15)


if __name__ == "__main__":
    sys.exit(main())
