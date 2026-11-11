#!/usr/bin/env python3
"""Provision public-only Ed25519 key for a ZonderqOS release build.

The release private key must stay outside the repository and outside the ISO.
Generate a key pair locally:
  umask 077
  openssl genpkey -algorithm ED25519 -out ~/zonderq-release-private.key
  openssl pkey -in ~/zonderq-release-private.key -pubout -out ~/zonderq-release.pub
  python3 tools/provision-release-public-key.py --public-key ~/zonderq-release.pub

The generated .cs source is gitignored and included by the Cosmos project
through regular .NET wildcard Compile items. A build without it stays locked.
"""
import argparse
import pathlib
import re
import subprocess
import sys

DER_PREFIX = bytes.fromhex("302a300506032b6570032100")
OUTPUT = pathlib.Path(__file__).resolve().parents[1] / "source/Core/Updates/TrustedReleaseKey.Provisioned.g.cs"


def public_key_bytes(path: pathlib.Path) -> bytes:
    proc = subprocess.run(
        ["openssl", "pkey", "-pubin", "-in", str(path), "-outform", "DER"],
        check=True, capture_output=True,
    )
    data = proc.stdout
    if len(data) != len(DER_PREFIX) + 32 or not data.startswith(DER_PREFIX):
        raise ValueError("Expected an Ed25519 SubjectPublicKeyInfo PEM public key")
    return data[len(DER_PREFIX):]


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--public-key", type=pathlib.Path, required=True)
    p.add_argument("--output", type=pathlib.Path, default=OUTPUT)
    args = p.parse_args()
    try:
        public = public_key_bytes(args.public_key)
        if public == bytes(32):
            raise ValueError("All-zero public key refused")
        destination = args.output
        destination.parent.mkdir(parents=True, exist_ok=True)
        contents = (
            "// Generated from a PUBLIC Ed25519 key. NEVER paste private keys.\n"
            "namespace ZonderqOS.SystemCore.Updates\n"
            "{\n"
            "    internal static partial class TrustedReleaseKey\n"
            "    {\n"
            "        static partial void SupplyPublicKey(ref string publicKeyHex)\n"
            "        {\n"
            '            publicKeyHex = "' + public.hex() + '";\n'
            "        }\n"
            "    }\n"
            "}\n"
        )
        destination.write_text(contents, encoding="utf-8")
        print(f"Release PUBLIC key fingerprint SHA256 must be recorded separately.")
        print(f"Generated: {destination}")
        return 0
    except (ValueError, OSError, subprocess.CalledProcessError) as exc:
        print("FAIL: cannot provision public key: " + str(exc), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
