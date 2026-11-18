#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

ISO_PATH="$ROOT_DIR/output-x64/ZonderqOS.iso"
ELF_PATH="$ROOT_DIR/output-x64/ZonderqOS.elf"
OUT_DIR="${1:-/tmp/zonderq_ram_soak_60min}"

if [[ ! -f "$ISO_PATH" || ! -f "$ELF_PATH" ]]; then
    echo "[SOAK] Brak $ISO_PATH lub $ELF_PATH. Uruchamiam cosmos build -a x64..."
    cosmos build -a x64
fi

COMMIT_SHA="$(git rev-parse HEAD)"
BRANCH_NAME="$(git branch --show-current)"
ISO_SHA256="$(sha256sum "$ISO_PATH" | awk '{print $1}')"

mkdir -p "$OUT_DIR"
cat > "$OUT_DIR/meta.txt" <<EOF
branch=$BRANCH_NAME
commit=$COMMIT_SHA
iso=$ISO_PATH
iso_sha256=$ISO_SHA256
vm_config=-M q35 -accel tcg -cpu Nehalem -smp 1 -m 8192M -vga std
requested_sample_span_sec=3600
qemu_wall_min_duration_sec=3780
EOF

echo "[SOAK] Branch: $BRANCH_NAME ($COMMIT_SHA)"
echo "[SOAK] ISO: $ISO_PATH (sha256=$ISO_SHA256)"
echo "[SOAK] Out: $OUT_DIR"

python3 -u "$ROOT_DIR/tools/measure_scenarios.py" \
    --scenario A \
    --smp 1 \
    --ram 8192M \
    --duration 3780 \
    --iso "$ISO_PATH" \
    --kernel "$ELF_PATH" \
    --out-dir "$OUT_DIR"

python3 - "$OUT_DIR" "$BRANCH_NAME" "$COMMIT_SHA" "$ISO_SHA256" <<'PY'
import json
import sys
from pathlib import Path

out_dir = Path(sys.argv[1])
branch = sys.argv[2]
commit = sys.argv[3]
iso_sha = sys.argv[4]
serial_log = out_dir / "serial.log"

samples = []
for line in serial_log.read_text(errors="ignore").splitlines():
    if not line.startswith("[MEM_SAMPLE]"):
        continue
    d = {}
    for part in line.split()[1:]:
        if "=" in part:
            k, v = part.split("=", 1)
            try:
                d[k] = float(v) if "." in v else int(v)
            except ValueError:
                d[k] = v
    if "sec" in d and "used_mb" in d:
        samples.append(d)

if not samples:
    raise SystemExit("[SOAK][FAIL] Brak próbek [MEM_SAMPLE] w serial.log")

t0 = samples[0]["sec"]
span = samples[-1]["sec"] - t0
if span < 3600:
    raise SystemExit(
        f"[SOAK][FAIL] Niepełne 60 min od pierwszej próbki: {span}s < 3600s"
    )

def at_or_after(target_offset):
    target = t0 + target_offset
    for sample in samples:
        if sample["sec"] >= target:
            return sample
    raise SystemExit(f"[SOAK][FAIL] Brak próbki dla T+{target_offset}s")

marks = {
    0: samples[0],
    10: at_or_after(600),
    20: at_or_after(1200),
    30: at_or_after(1800),
    60: at_or_after(3600),
}

base = marks[0]["used_mb"]
lines = [
    f"# ZOnderqOS 60-min QEMU RAM Soak Report",
    f"",
    f"- Branch: `{branch}`",
    f"- Commit: `{commit}`",
    f"- ISO SHA-256: `{iso_sha}`",
    f"- VM: `-M q35 -accel tcg -cpu Nehalem -smp 1 -m 8192M`",
    f"- Samples captured: `{len(samples)}` (span `{span}s`, >=3600s verified)",
    f"",
    f"| Czas | sec | Used RAM (MiB) | Przyrost od T=0 (MiB) | p_gc | gc_comm (KB) | gc_heap (KB) | gc_gen | threads |",
    f"|---|---:|---:|---:|---:|---:|---:|---:|---:|",
]
for m in (0, 10, 20, 30, 60):
    s = marks[m]
    delta = s["used_mb"] - base
    lines.append(
        f"| {m} min | {s['sec']} | {s['used_mb']:.2f} | {delta:+.2f} | "
        f"{s.get('p_gc', 'N/A')} | {s.get('gc_comm_kb', 'N/A')} | {s.get('gc_heap_kb', 'N/A')} | "
        f"{s.get('gc_gen', 'N/A')} | {s.get('threads', 'N/A')} |"
    )

d0_10 = marks[10]["used_mb"] - marks[0]["used_mb"]
d10_20 = marks[20]["used_mb"] - marks[10]["used_mb"]
d20_30 = marks[30]["used_mb"] - marks[20]["used_mb"]
d30_60 = marks[60]["used_mb"] - marks[30]["used_mb"]
d0_60 = marks[60]["used_mb"] - marks[0]["used_mb"]
span_min = max(1e-6, (marks[60]["sec"] - marks[0]["sec"]) / 60.0)
span_last30_min = max(1e-6, (marks[60]["sec"] - marks[30]["sec"]) / 60.0)

lines += [
    f"",
    f"## Przyrosty",
    f"- T=0 -> T=10: `{d0_10:+.2f} MiB`",
    f"- T=10 -> T=20: `{d10_20:+.2f} MiB`",
    f"- T=20 -> T=30: `{d20_30:+.2f} MiB`",
    f"- T=30 -> T=60: `{d30_60:+.2f} MiB`",
    f"- T=0 -> T=60: `{d0_60:+.2f} MiB`",
    f"- Średnie tempo (0->60): `{d0_60 / span_min:+.4f} MiB/min`",
    f"- Tempo w ostatnich 30 min (30->60): `{d30_60 / span_last30_min:+.4f} MiB/min`",
    f"",
    f"**WERDYKT**: {'PASS' if d0_60 <= 10.0 else 'CHECK'} (delta={d0_60:+.2f} MiB)",
]

report_path = out_dir / "report.md"
report_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
print("\n".join(lines))
PY
