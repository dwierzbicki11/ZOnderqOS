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


def reconnect_request(cycle: int) -> bytes:
    if cycle not in (1, 2):
        raise ValueError("reconnect cycle must be 1 or 2")
    return f"ZONDERQ3095-TCP-RECONNECT-{cycle}\n".encode("ascii")


def reconnect_response(cycle: int) -> bytes:
    if cycle not in (1, 2):
        raise ValueError("reconnect cycle must be 1 or 2")
    return f"ZONDERQ3095-TCP-RESTORED-{cycle}\n".encode("ascii")


def serve(port: int, reconnects: int = 0) -> None:
    """Extra sessions are opt-in and occur only after QMP link flaps."""
    if reconnects not in (0, 1, 2):
        raise ValueError("invalid reconnect count")
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
        listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        listener.bind(("127.0.0.1", port))
        listener.listen(1)
        listener.settimeout(120 if reconnects else 40)
        print(f"[NET3095-N4-HOST] READY port={port}", flush=True)
        for cycle in range(reconnects + 1):
            connection, _peer = listener.accept()
            with connection:
                connection.settimeout(8)
                request = REQUEST if cycle == 0 else reconnect_request(cycle)
                response = RESPONSE if cycle == 0 else reconnect_response(cycle)
                received = bytearray()
                while len(received) < len(request):
                    chunk = connection.recv(len(request) - len(received))
                    if not chunk:
                        break
                    received.extend(chunk)
                if bytes(received) != request:
                    raise ValueError(f"unexpected TCP request cycle={cycle} length={len(received)}")
                connection.sendall(response)
                if cycle:
                    print(f"[NET3095-N4-HOST] RECONNECT_PASS cycle={cycle}", flush=True)
                else:
                    print("[NET3095-N4-HOST] PASS request/reply", flush=True)


def self_test() -> None:
    """Exercise real loopback sockets, two reconnections and port rebind."""
    import threading
    import time

    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
        probe.bind(("127.0.0.1", 0))
        port = probe.getsockname()[1]
    for reconnects in (0, 2, 0):
        errors = []
        def run_server():
            try:
                serve(port, reconnects)
            except Exception as exc:
                errors.append(exc)
        worker = threading.Thread(target=run_server, daemon=True)
        worker.start()
        for cycle in range(reconnects + 1):
            connection = None
            for _ in range(100):
                try:
                    connection = socket.create_connection(("127.0.0.1", port), timeout=1)
                    break
                except ConnectionRefusedError:
                    time.sleep(0.02)
            if connection is None:
                raise AssertionError("test server did not accept a connection")
            with connection:
                connection.settimeout(5)
                request = REQUEST if cycle == 0 else reconnect_request(cycle)
                response = RESPONSE if cycle == 0 else reconnect_response(cycle)
                connection.sendall(request)
                data = bytearray()
                while len(data) < len(response):
                    part = connection.recv(len(response) - len(data))
                    if not part:
                        break
                    data.extend(part)
                if bytes(data) != response:
                    raise AssertionError(f"incorrect host TCP response cycle={cycle}")
        worker.join(timeout=5)
        if worker.is_alive() or errors:
            raise AssertionError(f"loopback server failed reconnects={reconnects}: {errors}")
    # Exercise the actual socket server's fail-closed request path, not just
    # pure payload helpers. A truncated or wrong request must never receive
    # the success response or produce a PASS marker.
    for malformed in (b"WRONG-REQUEST\\n", REQUEST[:-1]):
        errors = []
        def reject_server():
            try:
                serve(port)
            except Exception as exc:
                errors.append(exc)
        worker = threading.Thread(target=reject_server, daemon=True)
        worker.start()
        connection = None
        for _ in range(100):
            try:
                connection = socket.create_connection(("127.0.0.1", port), timeout=1)
                break
            except ConnectionRefusedError:
                time.sleep(0.02)
        if connection is None:
            raise AssertionError("negative test server did not start")
        with connection:
            connection.settimeout(5)
            connection.sendall(malformed)
            connection.shutdown(socket.SHUT_WR)
            if connection.recv(1):
                raise AssertionError("server replied to malformed TCP request")
        worker.join(timeout=5)
        if worker.is_alive() or len(errors) != 1 or not isinstance(errors[0], ValueError):
            raise AssertionError(f"malformed request was not rejected: {errors}")
    assert reconnect_request(1) != reconnect_request(2)
    assert reconnect_response(1) != reconnect_response(2)
    for bad in (0, 3):
        for make in (reconnect_request, reconnect_response):
            try:
                make(bad)
            except ValueError:
                pass
            else:
                raise AssertionError("accepted invalid reconnect cycle")
    print("[NET3095-N4-HOST-CONTRACT] PASS initial echo, two real TCP reconnects and sequential rebind")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=18095)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--reconnects", type=int, choices=(0, 1, 2), default=0)
    args = parser.parse_args()
    if not 1024 <= args.port <= 65535:
        parser.error("unprivileged port required")
    try:
        if args.self_test:
            self_test()
        else:
            serve(args.port, args.reconnects)
    except (OSError, ValueError, AssertionError) as exc:
        sys.exit(f"[NET3095-N4-HOST][FAIL] {exc}")
