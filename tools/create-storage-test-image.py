#!/usr/bin/env python3
"""Create a fresh disposable MBR/FAT32 fixture; never open an existing target."""
import argparse
import json
import struct
from pathlib import Path

SECTOR = 512
DISK_SECTORS = 131072  # 64 MiB
START = 2048
TOTAL = DISK_SECTORS - START
RESERVED = 32
FAT_SECTORS = 1024
DATA = RESERVED + 2 * FAT_SECTORS
CLUSTERS = TOTAL - DATA
PAYLOAD = bytes(range(256)) * 2 + b"ZonderqOS binary storage fixture\x00\xff"


def put32(buf, offset, value):
    struct.pack_into('<I', buf, offset, value)


def build(directory, scenario='normal'):
    if scenario not in ('normal', 'full', 'efi'):
        raise ValueError('unknown scenario')
    directory = Path(directory)
    # Exclusive directory creation also rejects existing directories and symlinks.
    directory.mkdir()
    image = directory / 'storage.raw'
    fat = bytearray(FAT_SECTORS * SECTOR)
    for cluster, value in ((0, 0x0FFFFFF8), (1, 0x0FFFFFFF),
                           (2, 0x0FFFFFFF), (3, 4), (4, 0x0FFFFFFF)):
        put32(fat, cluster * 4, value)
    full = scenario == 'full'
    if full:
        for cluster in range(5, CLUSTERS + 2):
            put32(fat, cluster * 4, cluster + 1 if cluster < CLUSTERS + 1 else 0x0FFFFFFF)
    free = 0 if full else CLUSTERS - 3
    boot = bytearray(SECTOR)
    boot[:11] = b'\xeb\x58\x90MSWIN4.1'
    struct.pack_into('<HBHBHHBHHHII', boot, 11,
                     SECTOR, 1, RESERVED, 2, 0, 0, 0xF8, 0, 63, 255, START, TOTAL)
    struct.pack_into('<IHHIHH', boot, 36, FAT_SECTORS, 0, 0, 2, 1, 6)
    boot[64] = 0x80
    boot[66] = 0x29
    put32(boot, 67, 0x5A4F4E44)
    boot[71:82] = b'ZONDERQDATA'
    boot[82:90] = b'FAT32   '
    boot[510:512] = b'\x55\xaa'
    info = bytearray(SECTOR)
    put32(info, 0, 0x41615252)
    put32(info, 484, 0x61417272)
    put32(info, 488, free)
    put32(info, 492, 0xFFFFFFFF if full else 5)
    put32(info, 508, 0xAA550000)
    mbr = bytearray(SECTOR)
    mbr[450] = 0xEF if scenario == 'efi' else 0x0C
    mbr[447:450] = mbr[451:454] = b'\xfe\xff\xff'
    struct.pack_into('<II', mbr, 454, START, TOTAL)
    mbr[510:512] = b'\x55\xaa'
    root = bytearray(SECTOR)
    root[:11] = b'ZONDERQDATA'
    root[11] = 0x08

    def entry(offset, name, cluster, size):
        root[offset:offset + 11] = name
        root[offset + 11] = 0x20
        struct.pack_into('<H', root, offset + 20, cluster >> 16)
        struct.pack_into('<H', root, offset + 26, cluster & 0xFFFF)
        put32(root, offset + 28, size)

    entry(32, b'PAYLOAD BIN', 3, len(PAYLOAD))
    if full:
        entry(64, b'FILLER  BIN', 5, (CLUSTERS - 3) * SECTOR)
    with image.open('xb') as stream:
        stream.truncate(DISK_SECTORS * SECTOR)

        def write(sector, data):
            stream.seek(sector * SECTOR)
            stream.write(data)

        write(0, mbr)
        for sector in (0, 6):
            write(START + sector, boot)
            write(START + sector + 1, info)
        write(START + RESERVED, fat)
        write(START + RESERVED + FAT_SECTORS, fat)
        write(START + DATA, root)
        write(START + DATA + 1, PAYLOAD)
    manifest = dict(scenario=scenario, image='storage.raw', sector_size=SECTOR,
                    partition_start=START, partition_sectors=TOTAL,
                    data_clusters=CLUSTERS, free_clusters=free,
                    allocated_clusters=CLUSTERS - free, payload_file='PAYLOAD.BIN',
                    payload_size=len(PAYLOAD), expected_mount=scenario != 'efi')
    with (directory / 'manifest.json').open('x') as stream:
        json.dump(manifest, stream, indent=2)
        stream.write('\n')
    return image


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path, help='new directory, must not exist')
    parser.add_argument('--scenario', choices=('normal', 'full', 'efi'), default='normal')
    args = parser.parse_args()
    print(build(args.directory, args.scenario))
