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


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=18095)
    args = parser.parse_args()
    if not 1024 <= args.port <= 65535:
        parser.error("unprivileged port required")
    try:
        serve(args.port)
    except (OSError, ValueError) as exc:
        sys.exit(f"[NET3095-N4-HOST][FAIL] {exc}")
