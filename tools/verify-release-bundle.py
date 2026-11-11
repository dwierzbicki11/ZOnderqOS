#!/usr/bin/env python3
"""Offline release bundle signer/verifier (HOST ONLY, not the kernel trust gate).

Sign exact manifest bytes with an external Ed25519 private key using OpenSSL 3.
Verify detached signature with the corresponding public key, then verify all
manifest-declared files. Never include a private key in the repository/ISO.

Usage:
  python3 tools/verify-release-bundle.py sign --bundle DIR --private-key release.key
  python3 tools/verify-release-bundle.py verify --bundle DIR --public-key release.pub

Until kernel-side signature verification is implemented, a successful host
verification MUST NOT unblock UpdateManager.ApplyUpdateStep.
"""
import argparse
import hashlib
import pathlib
import re
import subprocess
import sys

HEX64 = re.compile(r"[a-fA-F0-9]{64}\Z")
MAX_MANIFEST = 1024 * 1024
VALID_KEYS = {
    "VERSION", "BUILD", "ARCH", "TITLE", "DATE", "SEVERITY",
    "KERNEL_IMAGE", "KERNEL_SHA256", "PAYLOAD_DIR", "THEME",
    "WALLPAPER", "CHANGELOG", "COMPONENTS", "FILE",
    "UPDATE_FORMAT", "SIGNATURE_ALG",
}
SINGLE_KEYS = VALID_KEYS - {"FILE"}


def relative_path(value: str) -> pathlib.PurePosixPath:
    if not value or len(value) > 240 or "\\" in value or ":" in value:
        raise ValueError("Unsafe path: " + repr(value))
    parts = value.split("/")
    for part in parts:
        if (not part or part in {".", ".."} or part.endswith((".", " ")) or
                any(ord(ch) < 32 or ch in '<>|*?"' for ch in part)):
            raise ValueError("Unsafe path: " + repr(value))
    return pathlib.PurePosixPath(value)


def parse_manifest(raw: bytes):
    if not raw or len(raw) > MAX_MANIFEST or b"\x00" in raw:
        raise ValueError("Manifest empty, oversized or contains NUL")
    values = {}
    files = []
    seen_paths = set()
    for number, original in enumerate(raw.decode("utf-8").splitlines(), 1):
        line = original.strip()
        if not line or line.startswith("#"):
            continue
        if "=" not in line:
            raise ValueError(f"Malformed line {number}")
        key, value = (part.strip() for part in line.split("=", 1))
        if key not in VALID_KEYS:
            raise ValueError(f"Unknown manifest key {key!r} at line {number}")
        if key in SINGLE_KEYS:
            if key in values:
                raise ValueError(f"Duplicate manifest key {key!r}")
            values[key] = value
        else:
            fields = value.split(":")
            if len(fields) != 3 or not HEX64.fullmatch(fields[1]):
                raise ValueError(f"Invalid FILE at line {number}; SHA-256 and size required")
            path = relative_path(fields[0])
            if str(path).casefold() in seen_paths:
                raise ValueError(f"Duplicate FILE at line {number}")
            seen_paths.add(str(path).casefold())
            if not fields[2].isdigit():
                raise ValueError(f"Invalid FILE size at line {number}")
            files.append((path, fields[1].lower(), int(fields[2])))
            if len(files) > 1024:
                raise ValueError("Too many FILE records")
    for key in ("UPDATE_FORMAT", "SIGNATURE_ALG", "VERSION", "BUILD", "ARCH", "KERNEL_IMAGE", "KERNEL_SHA256", "PAYLOAD_DIR"):
        if not values.get(key):
            raise ValueError(f"Missing required field {key}")
    if values["UPDATE_FORMAT"] != "2" or values["SIGNATURE_ALG"] != "ED25519":
        raise ValueError("Signed package must be format 2 with ED25519")
    if values["ARCH"] != "x86_64":
        raise ValueError("Unsupported architecture")
    if not HEX64.fullmatch(values["KERNEL_SHA256"]):
        raise ValueError("Malformed kernel SHA-256")
    relative_path(values["KERNEL_IMAGE"])
    relative_path(values["PAYLOAD_DIR"])
    if not values["BUILD"].isdigit():
        raise ValueError("Invalid build")
    return values, files


def digest_file(path: pathlib.Path):
    h = hashlib.sha256()
    size = 0
    with path.open("rb") as source:
        while True:
            chunk = source.read(1024 * 1024)
            if not chunk:
                break
            h.update(chunk)
            size += len(chunk)
    return h.hexdigest(), size


def require_file(root: pathlib.Path, relative: pathlib.PurePosixPath):
    path = root.joinpath(*relative.parts)
    # Do not follow any symlink, including symlinked parent directories.
    current = root
    for part in relative.parts:
        current = current / part
        if current.is_symlink():
            raise ValueError("Symlink not permitted: " + str(current))
    if not path.is_file():
        raise ValueError("Missing file: " + str(path))
    if not path.resolve().is_relative_to(root.resolve()):
        raise ValueError("Path escapes bundle root: " + str(path))
    return path


def check_files(bundle: pathlib.Path, values, files):
    kernel = relative_path(values["KERNEL_IMAGE"])
    kernel_path = require_file(bundle, kernel)
    sha, _ = digest_file(kernel_path)
    if sha != values["KERNEL_SHA256"].lower():
        raise ValueError("Kernel SHA-256 mismatch")
    payload_root = relative_path(values["PAYLOAD_DIR"])
    expected = set()
    for relative, sha, length in files:
        full_relative = payload_root / relative
        path = require_file(bundle, full_relative)
        actual_hash, actual_size = digest_file(path)
        if actual_hash != sha or actual_size != length:
            raise ValueError("Payload digest/size mismatch: " + str(relative))
        expected.add(full_relative.as_posix().casefold())
    directory = bundle.joinpath(*payload_root.parts)
    if directory.exists():
        for path in directory.rglob("*"):
            if path.is_symlink():
                raise ValueError("Payload symlink rejected: " + str(path))
            if path.is_file() and path.relative_to(bundle).as_posix().casefold() not in expected:
                raise ValueError("Undeclared payload file: " + str(path))


def openssl(mode: str, bundle: pathlib.Path, key: pathlib.Path):
    manifest = bundle / "update.manifest"
    signature = bundle / "update.manifest.sig"
    args = ["openssl", "pkeyutl", "-" + mode, "-rawin", "-inkey", str(key)]
    if mode == "verify":
        args += ["-pubin"]
    args += ["-in", str(manifest)]
    if mode == "verify":
        args += ["-sigfile", str(signature)]
    else:
        args += ["-out", str(signature)]
    subprocess.run(args, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["sign", "verify"])
    parser.add_argument("--bundle", type=pathlib.Path, required=True)
    keys = parser.add_mutually_exclusive_group(required=True)
    keys.add_argument("--private-key", type=pathlib.Path)
    keys.add_argument("--public-key", type=pathlib.Path)
    args = parser.parse_args()
    if args.mode == "sign" and not args.private_key or args.mode == "verify" and not args.public_key:
        parser.error("sign needs --private-key; verify needs --public-key")
    try:
        bundle = args.bundle.resolve(strict=True)
        manifest = require_file(bundle, pathlib.PurePosixPath("update.manifest"))
        values, files = parse_manifest(manifest.read_bytes())
        check_files(bundle, values, files)
        openssl(args.mode, bundle, args.private_key or args.public_key)
    except (ValueError, OSError, subprocess.CalledProcessError, UnicodeDecodeError) as exc:
        print("FAIL: release bundle rejected: " + str(exc), file=sys.stderr)
        return 1
    print("PASS: manifest signature operation and payload integrity: " + args.mode)
    return 0


if __name__ == "__main__":
    sys.exit(main())
