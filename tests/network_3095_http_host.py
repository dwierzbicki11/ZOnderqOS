#!/usr/bin/env python3
"""Unprivileged loopback-only HTTP/1.0 fixture for the isolated N6 QEMU guest.

The guest reaches this listener at 10.0.2.2:18096 via QEMU user networking.
This fixture does not provide HTTPS, external connectivity, or GUI networking.
"""
from __future__ import annotations

import argparse
import socket
import sys
import time

PORT = 18096
REQUEST = b"GET /health HTTP/1.0\r\nHost: 10.0.2.2\r\nConnection: close\r\n\r\n"
BODY = (b"ZONDERQ3095-HTTP-OK\n" +
        b"A" * 1024 + b"B" * 1024 + b"C" * 1024 + b"D" * 1024)
RESPONSE = (b"HTTP/1.0 200 OK\r\n"
            b"Content-Type: text/plain\r\n"
            b"Content-Length: 4116\r\n"
            b"Connection: close\r\n\r\n" + BODY)
assert len(BODY) == 4116


def serve(port: int = PORT) -> None:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
        listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        listener.bind(("127.0.0.1", port))
        listener.listen(1)
        listener.settimeout(40)
        print(f"[NET3095-N6-HOST] READY port={port}", flush=True)
        connection, _peer = listener.accept()
        with connection:
            connection.settimeout(8)
            request = bytearray()
            while len(request) < len(REQUEST):
                chunk = connection.recv(len(REQUEST) - len(request))
                if not chunk:
                    break
                request.extend(chunk)
            if bytes(request) != REQUEST:
                raise ValueError(f"unexpected HTTP request length={len(request)}")
            # Force the guest through multiple TCP/IP packets and E1000E DMA
            # RX descriptors. The PCAP gate must independently establish the
            # segmentation, sequence continuity and exact 4 KiB body.
            connection.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
            boundary = RESPONSE.index(b"\r\n\r\n") + 4
            connection.sendall(RESPONSE[:boundary])
            for start in range(boundary, len(RESPONSE), 1024):
                time.sleep(0.1)
                connection.sendall(RESPONSE[start:start + 1024])
            print("[NET3095-N6-HOST] PASS HTTP/1.0 GET /health 200 split=6 body=4116", flush=True)


def self_test() -> None:
    import threading
    import time

    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
        probe.bind(("127.0.0.1", 0))
        port = probe.getsockname()[1]

    for attempt in range(2):
        errors = []

        def run_server() -> None:
            try:
                serve(port)
            except Exception as exc:
                errors.append(exc)

        worker = threading.Thread(target=run_server, daemon=True)
        worker.start()
        connection = None
        for _ in range(100):
            try:
                connection = socket.create_connection(("127.0.0.1", port), timeout=1)
                break
            except ConnectionRefusedError:
                time.sleep(0.02)
        if connection is None:
            raise AssertionError("HTTP listener did not become ready")
        with connection:
            connection.settimeout(5)
            connection.sendall(REQUEST)
            received = bytearray()
            while len(received) < len(RESPONSE):
                part = connection.recv(len(RESPONSE) - len(received))
                if not part:
                    break
                received.extend(part)
            if bytes(received) != RESPONSE:
                raise AssertionError("HTTP response differs from expected bytes")
        worker.join(timeout=5)
        if worker.is_alive() or errors:
            raise AssertionError(f"HTTP server failed on attempt {attempt + 1}: {errors}")
    print("[NET3095-N6-HOST-CONTRACT] PASS real loopback HTTP/1.0 split header/body and sequential rebind")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=PORT)
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
        sys.exit(f"[NET3095-N6-HOST][FAIL] {exc}")
