#!/usr/bin/env python3
"""Compare paired measurements; this does not collect guest metrics itself.

Input: JSON arrays of {run_id, environment, metrics: {name: number}}.
Environment is a nonempty dict of fixed QEMU/host/workload parameters.
Each metric must be a nonnegative finite number, and lower is better.
"""
import argparse
import json
import math
from pathlib import Path
from statistics import median


def nonnegative_median(values):
    """Avoid overflow in statistics.median's even-sized (a + b) / 2."""
    ordered = sorted(values)
    middle = len(ordered) // 2
    if len(ordered) % 2:
        return ordered[middle]
    return ordered[middle - 1] / 2 + ordered[middle] / 2


def environment_fingerprint(value):
    """Reject non-JSON/nonfinite settings and preserve JSON scalar types."""
    try:
        return json.dumps(value, sort_keys=True, allow_nan=False, separators=(',', ':'))
    except (TypeError, ValueError, OverflowError) as exc:
        raise ValueError('invalid environment/workload') from exc


def valid_metric(value):
    if type(value) not in (int, float) or value < 0:
        return False
    try:
        return math.isfinite(value)
    except OverflowError:
        # JSON accepts arbitrary-size integers, but float statistics do not.
        return False


def compare(before, after):
    if not isinstance(before, list) or not isinstance(after, list) or len(before) < 3 or len(before) != len(after):
        raise ValueError('at least three paired runs required')
    seen = set()
    metrics = None
    environment = None
    environment_key = None
    for index, (old, new) in enumerate(zip(before, after)):
        for record in (old, new):
            if not isinstance(record, dict) or not isinstance(record.get('run_id'), str) or not record['run_id'].strip():
                raise ValueError('nonempty run_id required')
            if not isinstance(record.get('environment'), dict) or not record['environment']:
                raise ValueError('nonempty environment required')
            if not isinstance(record.get('metrics'), dict) or not record['metrics']:
                raise ValueError('nonempty metrics required')
            if any(not isinstance(k, str) or not k or not valid_metric(v) for k, v in record['metrics'].items()):
                raise ValueError('invalid metric')
            record_environment_key = environment_fingerprint(record['environment'])
            if environment is None:
                environment = record['environment']
                environment_key = record_environment_key
            if record_environment_key != environment_key:
                raise ValueError('environment/workload mismatch')
            if metrics is None:
                metrics = set(record['metrics'])
            if set(record['metrics']) != metrics:
                raise ValueError('metric schema mismatch')
        if old['run_id'] != new['run_id'] or old['run_id'] in seen:
            raise ValueError(f'unpaired or duplicate run_id at index {index}')
        seen.add(old['run_id'])
    output = {'pairs': len(before), 'environment': environment, 'direction': 'lower_is_better', 'metrics': {}}
    for key in sorted(metrics):
        old = [r['metrics'][key] for r in before]
        new = [r['metrics'][key] for r in after]
        deltas = [b - a for a, b in zip(old, new)]
        old_median, new_median = nonnegative_median(old), nonnegative_median(new)
        change_percent = None if old_median == 0 else 100 * (new_median - old_median) / old_median
        if (not math.isfinite(old_median)
                or not math.isfinite(new_median)
                or not all(math.isfinite(x) for x in deltas)
                or not math.isfinite(new_median - old_median)
                or (change_percent is not None and not math.isfinite(change_percent))):
            raise ValueError(f'nonfinite derived metric: {key}')
        paired_median_delta = median(deltas)
        if not math.isfinite(paired_median_delta):
            raise ValueError(f'nonfinite derived metric: {key}')
        output['metrics'][key] = {
            'before_median': old_median, 'after_median': new_median,
            'delta': new_median - old_median,
            'change_percent': change_percent,
            'paired_median_delta': paired_median_delta,
            'paired_delta_range': [min(deltas), max(deltas)],
            'faster_pairs': sum(x < 0 for x in deltas),
            'slower_pairs': sum(x > 0 for x in deltas),
            'equal_pairs': sum(x == 0 for x in deltas),
        }
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('before', type=Path)
    parser.add_argument('after', type=Path)
    args = parser.parse_args()
    print(json.dumps(compare(json.loads(args.before.read_text()), json.loads(args.after.read_text())), indent=2))


if __name__ == '__main__':
    main()
