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
REQUEST = (b"GET /health HTTP/1.0\r\nHost: 10.0.2.2\r\n"
           b"Connection: close\r\nX-Zonderq-Attempt: 1\r\n\r\n")


def request_for_attempt(attempt: int) -> bytes:
    """Distinct equal-length guest TX bytes for each of six TCP sessions.

    The host and PCAP verifier both reject a replayed/stale TX descriptor
    even if the TCP sequence and response bytes happen to be valid.
    """
    if not 1 <= attempt <= 6:
        raise ValueError("HTTP request attempt outside 1..6")
    return REQUEST.replace(b"X-Zonderq-Attempt: 1\r\n",
                           f"X-Zonderq-Attempt: {attempt}\r\n".encode("ascii"))
def patterned_body(attempt: int) -> bytes:
    """Deterministic, printable byte-varying 4 KiB RX stress payload.

    Every position varies with both its offset and the TCP connection.
    A repeated or shifted DMA fragment *inside* a 1 KiB block is no
    longer masked by the old uniform A/B/C/D byte runs.
    """
    return bytes(33 + ((offset * 37 + (offset // 90) * 11 +
                        (offset // 1024) * 23 + attempt * 29) % 90)
                 for offset in range(4096))


BODY = b"ZONDERQ3095-HTTP-OK\n" + patterned_body(1)
RESPONSE = (b"HTTP/1.0 200 OK\r\n"
            b"Content-Type: text/plain\r\n"
            b"Content-Length: 4116\r\n"
            b"Connection: close\r\n\r\n" + BODY)
assert len(BODY) == 4116


def response_for_attempt(attempt: int) -> bytes:
    """Give every connection a distinct payload to detect stale RX DMA data.

    Every byte position changes between connections and varies within each
    1 KiB block. This detects shifted/stale fragments that uniform blocks
    could conceal. HTTP headers and total length remain unchanged.
    """
    if not 1 <= attempt <= 6:
        raise ValueError("HTTP fixture attempt outside 1..6")
    if attempt == 1:
        return RESPONSE
    first_body_block = RESPONSE.index(b"ZONDERQ3095-HTTP-OK\n") + len(b"ZONDERQ3095-HTTP-OK\n")
    return RESPONSE[:first_body_block] + patterned_body(attempt)


def serve(port: int = PORT) -> None:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
        listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        listener.bind(("127.0.0.1", port))
        listener.listen(2)
        listener.settimeout(40)
        print(f"[NET3095-N6-HOST] READY port={port}", flush=True)
        for attempt in range(1, 7):
            connection, _peer = listener.accept()
            with connection:
                connection.settimeout(8)
                request = bytearray()
                while len(request) < len(REQUEST):
                    chunk = connection.recv(len(REQUEST) - len(request))
                    if not chunk:
                        break
                    request.extend(chunk)
                if bytes(request) != request_for_attempt(attempt):
                    raise ValueError(f"unexpected HTTP request length={len(request)}")
                # Force the guest through multiple TCP/IP packets and E1000E DMA
                # RX descriptors. The PCAP gate must independently establish the
                # segmentation, sequence continuity and exact 4 KiB body.
                connection.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                response = response_for_attempt(attempt)
                boundary = response.index(b"\r\n\r\n") + 4
                connection.sendall(response[:boundary])
                for start in range(boundary, len(response), 1024):
                    time.sleep(0.1)
                    connection.sendall(response[start:start + 1024])
                label = ("PASS" if attempt == 1 else "REPEAT_PASS" if attempt == 2 else
                         "POST_BURST_PASS" if attempt == 3 else
                         f"POST_BURST_REPEAT_PASS attempt={attempt}")
                print("[NET3095-N6-HOST] " + label +
                      " HTTP/1.0 GET /health 200 split=6 body=4116", flush=True)
    

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
        for request_index in range(6):
            if request_index:
                connection = socket.create_connection(("127.0.0.1", port), timeout=1)
            with connection:
                connection.settimeout(5)
                connection.sendall(request_for_attempt(request_index + 1))
                received = bytearray()
                while len(received) < len(RESPONSE):
                    part = connection.recv(len(RESPONSE) - len(received))
                    if not part:
                        break
                    received.extend(part)
                if bytes(received) != response_for_attempt(request_index + 1):
                    raise AssertionError("HTTP response differs from expected attempt bytes")
        worker.join(timeout=5)
        if worker.is_alive() or errors:
            raise AssertionError(f"HTTP server failed on attempt {attempt + 1}: {errors}")
    assert request_for_attempt(1) == REQUEST
    assert len({request_for_attempt(i) for i in range(1, 7)}) == 6
    assert all(len(request_for_attempt(i)) == len(REQUEST) for i in range(1, 7))
    for invalid_attempt in (0, 7):
        try:
            request_for_attempt(invalid_attempt)
        except ValueError:
            pass
        else:
            raise AssertionError("accepted out-of-range HTTP TX attempt")
    assert len({response_for_attempt(i) for i in range(1, 7)}) == 6
    assert all(len(response_for_attempt(i)) == len(RESPONSE) for i in range(1, 7))
    body_start = RESPONSE.index(b"ZONDERQ3095-HTTP-OK\n") + len(b"ZONDERQ3095-HTTP-OK\n")
    for attempt in range(1, 7):
        body_blocks = response_for_attempt(attempt)[body_start:]
        assert len(body_blocks) == 4096
        assert body_blocks == patterned_body(attempt)
        for block in range(4):
            chunk = body_blocks[block * 1024:(block + 1) * 1024]
            assert len(set(chunk)) > 50, "uniform/low-diversity RX block"
        if attempt > 1:
            previous = patterned_body(attempt - 1)
            assert all(a != b for a, b in zip(body_blocks, previous))
    # The pattern must expose a one-byte shift even within one descriptor.
    for attempt in range(1, 7):
        block = patterned_body(attempt)[:1024]
        assert block[1:] != block[:-1]
    print("[NET3095-N6-HOST-CONTRACT] PASS six distinct HTTP RX payloads per server and sequential rebind")


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
