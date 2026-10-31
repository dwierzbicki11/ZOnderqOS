#!/usr/bin/env python3
"""CI-only deterministic TCP echo endpoint for QEMU's 10.0.2.2 host alias.

Runs as an unprivileged process on loopback. Never exposed by ZonderqOS GUI.
A real QEMU E1000E guest must complete the TCP handshake and exchange the
exact request/reply; serial markers alone cannot satisfy the PCAP gate.
"""
from __future__ import annotations

import argparse
import socket
import sys

REQUEST = b"ZONDERQ3095-TCP-PING\n"
RESPONSE = b"ZONDERQ3095-TCP-PONG\n"


def serve(port: int) -> None:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
        # N4 and N5 run sequentially on the same CI host/port. The first
        # connection can leave the local endpoint in TCP TIME_WAIT. Allow a
        # clean sequential rebind, but never SO_REUSEPORT or non-loopback bind.
        listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        listener.bind(("127.0.0.1", port))
        listener.listen(1)
        listener.settimeout(40)
        print(f"[NET3095-N4-HOST] READY port={port}", flush=True)
        connection, peer = listener.accept()
        with connection:
            connection.settimeout(8)
            received = bytearray()
            while len(received) < len(REQUEST):
                chunk = connection.recv(len(REQUEST) - len(received))
                if not chunk:
                    break
                received.extend(chunk)
            if bytes(received) != REQUEST:
                raise ValueError(f"unexpected TCP request length={len(received)}")
            connection.sendall(RESPONSE)
            print("[NET3095-N4-HOST] PASS request/reply", flush=True)


def self_test() -> None:
    """Exercise two real loopback TCP exchanges on the same port.

    The server actively closes each connection, leaving a possible TIME_WAIT
    socket. A second bind must still succeed without using SO_REUSEPORT.
    """
    import threading
    import time

    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
        probe.bind(("127.0.0.1", 0))
        port = probe.getsockname()[1]

    for attempt in range(2):
        errors = []

        def run_server():
            try:
                serve(port)
            except Exception as exc:
                errors.append(exc)

        worker = threading.Thread(target=run_server, daemon=True)
        worker.start()
        connection = None
        for _ in range(50):
            try:
                connection = socket.create_connection(("127.0.0.1", port), timeout=1)
                break
            except ConnectionRefusedError:
                time.sleep(0.02)
        if connection is None:
            raise AssertionError("test server did not accept a connection")
        with connection:
            connection.sendall(REQUEST)
            data = bytearray()
            while len(data) < len(RESPONSE):
                chunk = connection.recv(len(RESPONSE) - len(data))
                if not chunk:
                    break
                data.extend(chunk)
            if bytes(data) != RESPONSE:
                raise AssertionError("incorrect host TCP response")
        worker.join(timeout=5)
        if worker.is_alive() or errors:
            raise AssertionError(f"loopback server failed on pass {attempt + 1}: {errors}")
    print("[NET3095-N4-HOST-CONTRACT] PASS sequential loopback rebind after TCP close")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=18095)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if not 1024 <= args.port <= 65535:
        parser.error("unprivileged port required")
    try:
        if args.self_test:
            self_test()
        else:
            serve(args.port)
    except (OSError, ValueError, AssertionError) as exc:
        sys.exit(f"[NET3095-N4-HOST][FAIL] {exc}")
