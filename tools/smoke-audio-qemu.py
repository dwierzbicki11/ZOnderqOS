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

balance_spec = spec_from_file_location("audio_balance", ROOT / "tools/verify-audio-balance-capture.py")
balance_capture = module_from_spec(balance_spec)
balance_spec.loader.exec_module(balance_capture)

mute_spec = spec_from_file_location("audio_mute", ROOT / "tools/verify-audio-mute-capture.py")
mute_capture = module_from_spec(mute_spec)
mute_spec.loader.exec_module(mute_capture)

fade_spec = spec_from_file_location("audio_fade", ROOT / "tools/verify-audio-fade-capture.py")
fade_capture = module_from_spec(fade_spec)
fade_spec.loader.exec_module(fade_capture)

# Explicit pitch identity prevents one diagnostic command from being
# accidentally certified by another command's captured PCM.
EXPECTED_TONE_HZ = {
    "tone": 440,
    "stereosplit": 440,
    "wavtone": 440,
    "wavmono": 660,
    "wav44": 880,
    "wav16k": 1500,
    "wav22k": 1100,
    "wav24k": 740,
    "wav32k": 3520,
    "wavstereo32": 440,  # left; right checked separately by stereo validator
    "wav8": 1320,
    "wav24": 1760,
    "wav32": 2200,
    "wavext": 2640,
    "wavext20": 3080,
}


stereo_spec = spec_from_file_location("stereo_capture", ROOT / "tools/verify-audio-stereo-capture.py")
stereo_capture = module_from_spec(stereo_spec)
stereo_spec.loader.exec_module(stereo_capture)


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
            # QMP QKeyCode calls the space key "spc", not the literal " ".
            key = "ret" if character == "\n" else "spc" if character == " " else character
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
    parser.add_argument("--command", choices=("tone", "stereosplit", "wavtone", "wavmono", "wav44", "wav16k", "wav22k", "wav24k", "wav32k", "wavstereo32", "wav8", "wav24", "wav32", "wavext", "wavext20"), default="tone",
                        help="isolated guest command to execute; WAV probe never reads guest disks")
    parser.add_argument("--volume", type=int, default=100, help="software gain 0..100 percent")
    parser.add_argument("--balance", type=int, default=0, help="-100 left-only, +100 right-only")
    parser.add_argument("--fade-ms", type=int, choices=(0, 20), default=0,
                        help="opt-in 20ms stereo PCM fade, checked from actual WAV samples")
    args = parser.parse_args()
    if args.boot_seconds < 5 or args.boot_seconds > 120:
        parser.error("--boot-seconds must be 5..120")
    if not 0 <= args.volume <= 100:
        parser.error("--volume must be 0..100")
    if not -100 <= args.balance <= 100:
        parser.error("--balance must be -100..100")
    if args.balance and args.command != "stereosplit":
        parser.error("--balance capture requires --command stereosplit")
    if args.fade_ms and (args.command != "tone" or args.volume != 100 or args.balance):
        parser.error("--fade-ms capture requires tone at volume 100 and balance 0")
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
            qmp.type_line(f"volume {args.volume}")
            time.sleep(0.5)
            qmp.type_line(f"balance {args.balance}")
            time.sleep(0.5)
            qmp.type_line(f"fade {args.fade_ms}")
            time.sleep(0.5)
            qmp.type_line(args.command)
            time.sleep(4)
            try:
                qmp.execute("screendump", {"filename": str(out / "after.ppm")})
            except RuntimeError as exc:
                print(f"[AUDIO-QEMU] optional screenshot unavailable: {exc}")
            qmp.execute("quit")
            guest.wait(timeout=15)
            if guest.returncode != 0:
                raise RuntimeError(f"QEMU quit with status {guest.returncode}")
            if args.fade_ms:
                # Inspect actual PCM attack/release, not just a guest serial message.
                print(fade_capture.inspect(wav))
            elif args.volume == 0:
                print(mute_capture.inspect(wav))
            elif args.command == "stereosplit" and args.balance == 100:
                print(balance_capture.inspect(wav))
            elif args.command == "stereosplit":
                print(stereo_capture.inspect(wav))
            elif args.command == "wavstereo32":
                # Require both independently resampled channels, not just a 440 Hz tone.
                print(stereo_capture.inspect(wav))
            else:
                # The mono decoder probe uses a different pitch to prevent a
                # stray 440 Hz tone from masquerading as successful mono playback.
                print(capture.inspect(wav, expected_hz=EXPECTED_TONE_HZ[args.command]))
            print(f"[AUDIO-QEMU] PASS command={args.command} volume={args.volume} balance={args.balance} fade_ms={args.fade_ms} real QEMU HDA WAV capture; no physical speaker proof")
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
