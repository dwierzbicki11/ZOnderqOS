#!/usr/bin/env python3
"""Opt-in QMP keyboard probe for a disposable ZonderqOS FAT32 test image.

Never use with real user disks. The password below is a public, synthetic
test credential written only to the freshly created QEMU test volume.
A successful QMP send-key is not proof that the guest reached its desktop.
"""
import json
import socket
import sys
import time

TEST_PASSWORD = "gui2026test"
KEY_DELAY = 0.18


def send_command(channel, command):
    channel.write((json.dumps(command) + "\r\n").encode("ascii"))
    while True:
        reply = json.loads(channel.readline())
        if "error" in reply:
            raise RuntimeError("QMP command rejected: " + str(reply["error"]))
        if "return" in reply:
            return


def send_key(channel, code):
    send_command(channel, {
        "execute": "send-key",
        "arguments": {
            "keys": [{"type": "qcode", "data": code}],
            "hold-time": 80,
        },
    })
    time.sleep(KEY_DELAY)


def enter_password(channel):
    for code in TEST_PASSWORD:
        send_key(channel, code)
    send_key(channel, "ret")


def main():
    if len(sys.argv) != 3 or sys.argv[2] not in ("first", "second"):
        raise SystemExit("usage: qmp-gui-login.py <qmp-socket> <first|second>")

    with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as connection:
        connection.settimeout(15)
        connection.connect(sys.argv[1])
        channel = connection.makefile("rwb", buffering=0)
        greeting = json.loads(channel.readline())
        if "QMP" not in greeting:
            raise RuntimeError("QMP greeting missing")
        send_command(channel, {"execute": "qmp_capabilities"})

        if sys.argv[2] == "first":
            # InitialSetupManager: first Enter selects confirmation, second
            # submits a policy-compliant password on the disposable volume.
            enter_password(channel)
            time.sleep(0.5)
            enter_password(channel)
            time.sleep(4)

        # LoginScreenManager preselects the only root user and password field.
        enter_password(channel)

    print("[GUI-QMP] Sent synthetic setup/login keystrokes; framebuffer must be inspected separately.")


if __name__ == "__main__":
    main()
