#!/usr/bin/env python3
"""Builds a GitHub-Releases-ready Ed25519-signed ZonderqOS v2 OTA update bundle.

Usage:
  python3 tools/build-ota-release.py \
      --version 1.0.3 --build 3130 \
      --kernel output-x64/ZonderqOS.elf \
      --private-key /path/to/release.key \
      --out-dir output-ota-release \
      [--payload-dir /path/to/payload] [--arch x86_64]
"""
import argparse
import hashlib
import pathlib
import shutil
import subprocess
import sys
from datetime import date


def sha256_file(path: pathlib.Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        while True:
            chunk = f.read(65536)
            if not chunk:
                break
            h.update(chunk)
    return h.hexdigest()


def main() -> int:
    ap = argparse.ArgumentParser(description="Build signed ZonderqOS v2 OTA bundle for GitHub Releases.")
    ap.add_argument("--version", required=True)
    ap.add_argument("--build", required=True, type=int)
    ap.add_argument("--kernel", required=True, type=pathlib.Path)
    ap.add_argument("--private-key", required=True, type=pathlib.Path)
    ap.add_argument("--out-dir", required=True, type=pathlib.Path)
    ap.add_argument("--payload-dir", type=pathlib.Path, default=None)
    ap.add_argument("--arch", default="x86_64", choices=["x86_64", "arm64"])
    ap.add_argument("--title", default=None)
    ap.add_argument("--changelog", default="HTTPS OTA kernel and system update")
    args = ap.parse_args()

    if not args.kernel.is_file():
        raise SystemExit(f"Kernel binary not found: {args.kernel}")
    if not args.private_key.is_file():
        raise SystemExit(f"Ed25519 private key not found: {args.private_key}")

    out_dir = args.out_dir
    if out_dir.exists():
        shutil.rmtree(out_dir)
    out_dir.mkdir(parents=True)
    payload_out = out_dir / "payload"
    payload_out.mkdir()

    kernel_dest = out_dir / "ZonderqOS.elf"
    shutil.copy2(args.kernel, kernel_dest)
    kernel_sha = sha256_file(kernel_dest)

    file_entries = []
    flat_assets = []
    if args.payload_dir and args.payload_dir.is_dir():
        for src in sorted(args.payload_dir.rglob("*")):
            if not src.is_file():
                continue
            rel = src.relative_to(args.payload_dir).as_posix()
            dst = payload_out / rel
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dst)
            fsha = sha256_file(dst)
            fsize = dst.stat().st_size
            file_entries.append(f"FILE={rel}:{fsha}:{fsize}")
            # Also copy flat GitHub Release asset name alongside nested payload/.
            flat_name = "payload__" + rel.replace("/", "_")
            shutil.copy2(src, out_dir / flat_name)
            flat_assets.append(flat_name)

    title = args.title or f"ZonderqOS {args.version} OTA Update"
    manifest_lines = [
        "UPDATE_FORMAT=2",
        "SIGNATURE_ALG=ED25519",
        f"VERSION={args.version}",
        f"BUILD={args.build}",
        f"ARCH={args.arch}",
        f"TITLE={title}",
        f"DATE={date.today().isoformat()}",
        "SEVERITY=Recommended",
        "KERNEL_IMAGE=ZonderqOS.elf",
        f"KERNEL_SHA256={kernel_sha}",
        "PAYLOAD_DIR=payload",
        f"CHANGELOG={args.changelog}",
        "COMPONENTS=ZonderqOS Kernel ELF;BootSlotManager A/B;HTTPS OTA Engine",
    ] + file_entries

    manifest_path = out_dir / "update.manifest"
    manifest_path.write_text("\n".join(manifest_lines) + "\n", encoding="utf-8")

    # Sign exact manifest bytes with Ed25519 using OpenSSL 3.
    sig_path = out_dir / "update.manifest.sig"
    subprocess.run(
        [
            "openssl", "pkeyutl", "-sign",
            "-inkey", str(args.private_key),
            "-rawin",
            "-in", str(manifest_path),
            "-out", str(sig_path),
        ],
        check=True,
    )

    print(f"[OTA-BUILD] Signed v2 OTA bundle created in: {out_dir}")
    print(f"  Upload to GitHub Release assets:")
    print(f"    - {manifest_path.name}")
    print(f"    - {sig_path.name}")
    print(f"    - {kernel_dest.name} (SHA-256: {kernel_sha})")
    for fa in flat_assets:
        print(f"    - {fa}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
