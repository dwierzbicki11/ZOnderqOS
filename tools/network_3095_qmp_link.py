#!/usr/bin/env python3
"""Opt-in QEMU QMP link toggle for the isolated E1000E N6 recovery test.

This controls only the named QEMU netdev on a private UNIX socket. A successful
QMP response is NOT proof that the guest observed the transition or that the
NIC can transmit after it. Guest UART and PCAP are checked independently.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import socket
import tempfile
import threading


def read_message(reader):
    while True:
        line = reader.readline()
        if not line:
            raise ValueError("QMP closed the connection before a response")
        message = json.loads(line)
        if not isinstance(message, dict):
            raise ValueError("QMP returned a non-object")
        if "event" in message:
            continue
        return message


def command(sock, reader, name: str, arguments: dict | None = None):
    request = {"execute": name, "id": name}
    if arguments is not None:
        request["arguments"] = arguments
    sock.sendall((json.dumps(request) + "\r\n").encode("ascii"))
    reply = read_message(reader)
    if reply.get("id") != name or "error" in reply or "return" not in reply:
        raise ValueError(f"QMP rejected {name}: {reply}")
    return reply["return"]


def set_link(path: Path, up: bool) -> None:
    with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as sock:
        sock.settimeout(5)
        sock.connect(str(path))
        with sock.makefile("rb") as reader:
            greeting = read_message(reader)
            if not isinstance(greeting.get("QMP"), dict):
                raise ValueError("missing QMP greeting")
            command(sock, reader, "qmp_capabilities")
            # The isolated smoke script creates -netdev user,id=net0.
            command(sock, reader, "set_link", {"name": "net0", "up": up})


def self_test() -> None:
    with tempfile.TemporaryDirectory() as temp:
        path = Path(temp) / "qmp.sock"
        ready = threading.Event()
        captured = []
        def server():
            with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as listener:
                listener.bind(str(path))
                listener.listen(1)
                ready.set()
                conn, _ = listener.accept()
                with conn:
                    with conn.makefile("rb") as reader:
                        conn.sendall(b'{"QMP":{"version":{},"capabilities":[]}}\r\n')
                        for expected in ("qmp_capabilities", "set_link"):
                            req = json.loads(reader.readline())
                            assert req["execute"] == expected
                            captured.append(req)
                            conn.sendall((json.dumps({"return": {}, "id": expected}) +
                                          "\r\n").encode("ascii"))
        thread = threading.Thread(target=server, daemon=True)
        thread.start()
        assert ready.wait(2)
        set_link(path, False)
        thread.join(timeout=2)
        assert not thread.is_alive()
        assert captured[1]["arguments"] == {"name": "net0", "up": False}
    # Reject a successful-looking reply with the wrong request ID.
    import io
    try:
        read_message(io.BytesIO(b'{"event":"STOP"}\n{"return":{}}\n'))
    except Exception:
        raise AssertionError("unexpected event parsing error")
    try:
        command(type("Socket", (), {"sendall": lambda self, b: None})(),
                io.BytesIO(b'{"return":{},"id":"wrong"}\n'), "set_link")
    except ValueError:
        pass
    else:
        raise AssertionError("accepted an unrelated QMP response")
    print("[NET3095-QMP-LINK-CONTRACT] PASS mocked QMP handshake and negative reply")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--socket", type=Path)
    parser.add_argument("--state", choices=("down", "up"))
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        if args.socket is None or args.state is None:
            parser.error("--socket and --state are required")
        try:
            set_link(args.socket, args.state == "up")
        except (OSError, ValueError, TimeoutError) as exc:
            parser.exit(1, f"[NET3095-QMP-LINK][FAIL] {exc}\n")
        print(f"[NET3095-QMP-LINK] QMP_ACCEPTED state={args.state}; guest proof pending")
