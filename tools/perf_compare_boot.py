#!/usr/bin/env python3
"""Compare paired guest initialization baselines; never infer full boot speed."""
import argparse
import json
from pathlib import Path
from statistics import median


def compare(before, after):
    for label, records in (("before", before), ("after", after)):
        if not isinstance(records, list) or len(records) < 3:
            raise ValueError(f"{label}: need at least three independent runs")
        for record in records:
            if not isinstance(record, dict) or type(record.get("first_boot_ms")) is not int or type(record.get("second_boot_ms")) is not int:
                raise ValueError(f"{label}: invalid guest timing")
            if min(record["first_boot_ms"], record["second_boot_ms"]) <= 0:
                raise ValueError(f"{label}: nonpositive guest timing")
    if len(before) != len(after):
        raise ValueError("unequal run counts; require paired measurements")
    # Optional explicit run identities prevent accidentally pairing reordered CI runs.
    ids_before = [record.get("run_id") for record in before]
    ids_after = [record.get("run_id") for record in after]
    if any(value is not None for value in ids_before + ids_after):
        if (any(not isinstance(value, str) or not value.strip() or value != value.strip() for value in ids_before + ids_after)
                or len(set(ids_before)) != len(ids_before)
                or len(set(ids_after)) != len(ids_after)):
            raise ValueError("run_id must be a nonempty, whitespace-trimmed unique string in every run")
        if ids_before != ids_after:
            raise ValueError("run_id order differs; align paired runs before comparison")
    result = {"scope": "guest BeforeRun-to-login only; not firmware/GUI/full boot", "pairs": len(before)}
    for key in ("first_boot_ms", "second_boot_ms"):
        old = median(item[key] for item in before)
        new = median(item[key] for item in after)
        result[key] = {"before_median_ms": old, "after_median_ms": new,
                       "delta_ms": new - old, "change_percent": (new - old) * 100 / old}
    # Pairwise deltas expose run-to-run reversals hidden by a median-of-medians.
    for key in ("first_boot_ms", "second_boot_ms"):
        deltas = [new[key] - old[key] for old, new in zip(before, after)]
        result[key]["paired_median_delta_ms"] = median(deltas)
        result[key]["faster_pairs"] = sum(delta < 0 for delta in deltas)
        result[key]["slower_pairs"] = sum(delta > 0 for delta in deltas)
        result[key]["equal_pairs"] = sum(delta == 0 for delta in deltas)
        result[key]["paired_delta_range_ms"] = [min(deltas), max(deltas)]
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("before", type=Path)
    parser.add_argument("after", type=Path)
    args = parser.parse_args()
    print(json.dumps(compare(json.loads(args.before.read_text()),
                             json.loads(args.after.read_text())), indent=2))


if __name__ == "__main__":
    main()
