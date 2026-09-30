#!/usr/bin/env python3
# Reads what scripts/bench-compare.sh measured of two builds and says, for each benchmark,
# whether the second is faster, slower or the same: a change is called only when it is at
# least the threshold and a Mann-Whitney test says the two sets of timings differ.
#
#   bench-compare.py RUN_DIR THRESHOLD_PERCENT [--json]
#
# RUN_DIR holds raw/base and raw/head: BenchmarkDotNet's *-report-full-compressed.json
# under each, and web/*.json, one hear.mjs status per run. Writes report.md and
# report.json into RUN_DIR and prints one of them. Standard library only.

import glob
import json
import math
import os
import re
import statistics
import sys

P_CALLED = 0.05


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    run, threshold = sys.argv[1], float(sys.argv[2])
    as_json = '--json' in sys.argv[3:]

    meta = read_json(os.path.join(run, 'meta.json'))
    base, head = (os.path.join(run, 'raw', side) for side in ('base', 'head'))

    rows = compare(benchmarks(base), benchmarks(head), threshold, lower_is_better=True)
    web = compare(web_speeds(base), web_speeds(head), threshold, lower_is_better=False)

    report = {'base': meta['base'], 'head': meta['head'], 'threshold': threshold, 'benchmarks': rows, 'web': web}

    with open(os.path.join(run, 'report.json'), 'w', encoding='utf-8') as out:
        json.dump(report, out, indent=2)

    text = markdown(report)

    with open(os.path.join(run, 'report.md'), 'w', encoding='utf-8') as out:
        out.write(text)

    print(json.dumps(report, indent=2) if as_json else text)


def read_json(path):
    with open(path, encoding='utf-8') as source:
        return json.load(source)


def benchmarks(side):
    """Nanoseconds an operation, each iteration BenchmarkDotNet kept, and bytes allocated an operation, by benchmark."""
    found = {}

    for path in glob.glob(os.path.join(side, '**', '*-report-full-compressed.json'), recursive=True):
        for bench in read_json(path)['Benchmarks']:
            entry = found.setdefault(bench['FullName'], {'samples': [], 'allocated': set()})
            entry['samples'] += [
                m['Nanoseconds'] / m['Operations']
                for m in bench['Measurements']
                if m['IterationMode'] == 'Workload' and m['IterationStage'] == 'Result' and m['Operations'] > 0
            ]
            allocated = bench['Memory'].get('BytesAllocatedPerOperation')
            if allocated is not None:
                entry['allocated'].add(allocated)

    return found


def web_speeds(side):
    """Times real time the web viewer's sound rendered, each run, by preset."""
    found = {}

    for path in glob.glob(os.path.join(side, 'web', '*.json')):
        preset = re.sub(r'^r\d+-', '', os.path.splitext(os.path.basename(path))[0]).replace('_', ' ')
        speed = read_json(path).get('speed')
        if speed:
            found.setdefault(f'web: {preset}', {'samples': [], 'allocated': set()})['samples'].append(speed)

    return found


def compare(base, head, threshold, lower_is_better):
    rows = []

    for name in sorted(base.keys() | head.keys()):
        b, h = base.get(name), head.get(name)

        if not b or not h or not b['samples'] or not h['samples']:
            rows.append({'name': name, 'verdict': 'only in base' if b else 'only in head'})
            continue

        mb, mh = statistics.median(b['samples']), statistics.median(h['samples'])

        # As a change in time, so less is faster whichever way the benchmark counts.
        change = (mh / mb - 1) * 100 if lower_is_better else (mb / mh - 1) * 100
        p = mann_whitney(b['samples'], h['samples'])

        verdict = 'same'
        if p < P_CALLED and abs(change) >= threshold:
            verdict = 'faster' if change < 0 else 'slower'

        row = {
            'name': name,
            'base': mb,
            'head': mh,
            'change': round(change, 1),
            'p': round(p, 4),
            'samples': [len(b['samples']), len(h['samples'])],
            'verdict': verdict,
        }

        if b['allocated'] or h['allocated']:
            row['allocated'] = [max(b['allocated'], default=None), max(h['allocated'], default=None)]
            if row['allocated'][0] != row['allocated'][1]:
                row['verdict'] += ', allocates ' + ('more' if (row['allocated'][1] or 0) > (row['allocated'][0] or 0) else 'less')

        rows.append(row)

    return rows


def mann_whitney(a, b):
    """Two-sided p that a and b come from one distribution: exact for small samples without ties, normal otherwise."""
    m, n = len(a), len(b)
    pooled = sorted([(v, 0) for v in a] + [(v, 1) for v in b])
    values = [v for v, _ in pooled]
    ranks = midranks(values)
    u = sum(r for r, (_, side) in zip(ranks, pooled) if side == 0) - m * (m + 1) / 2
    tied = len(set(values)) < len(values)

    if m * n <= 400 and math.comb(m + n, m) <= 2_000_000 and not tied:
        extreme = min(u, m * n - u)
        counts = u_counts(m, n)
        total = math.comb(m + n, m)
        return min(1.0, 2 * sum(counts[:int(extreme) + 1]) / total)

    ties = sum(t ** 3 - t for t in run_lengths(values))
    sigma = math.sqrt(m * n / 12 * ((m + n + 1) - ties / ((m + n) * (m + n - 1))))
    if sigma == 0:
        return 1.0
    z = (abs(u - m * n / 2) - 0.5) / sigma
    return min(1.0, math.erfc(max(z, 0) / math.sqrt(2)))


def u_counts(m, n):
    """How many orderings of m and n give each U, 0 to m·n."""
    table = {}

    def count(i, j):
        if (i, j) not in table:
            if i == 0 or j == 0:
                table[i, j] = [1] + [0] * (i * j)
            else:
                shifted = [0] * j + count(i - 1, j)
                rest = count(i, j - 1)
                table[i, j] = [(shifted[k] if k < len(shifted) else 0) + (rest[k] if k < len(rest) else 0) for k in range(i * j + 1)]
        return table[i, j]

    return count(m, n)


def midranks(sorted_values):
    ranks, i = [], 0
    while i < len(sorted_values):
        j = i
        while j + 1 < len(sorted_values) and sorted_values[j + 1] == sorted_values[i]:
            j += 1
        ranks += [(i + j) / 2 + 1] * (j - i + 1)
        i = j + 1
    return ranks


def run_lengths(sorted_values):
    lengths, i = [], 0
    while i < len(sorted_values):
        j = i
        while j + 1 < len(sorted_values) and sorted_values[j + 1] == sorted_values[i]:
            j += 1
        lengths.append(j - i + 1)
        i = j + 1
    return lengths


def markdown(report):
    lines = [f"base {report['base']}  →  head {report['head']}  (a change is called at {report['threshold']:g}% and p < {P_CALLED})", '']

    if report['benchmarks']:
        lines += ['| Benchmark | Base | Head | Change | p | Allocated | Verdict |', '|---|---|---|---|---|---|---|']
        lines += [row_line(row, time) for row in report['benchmarks']]
        lines.append('')

    if report['web']:
        lines += ['| Web viewer sound | Base | Head | Change in time | p | Runs | Verdict |', '|---|---|---|---|---|---|---|']
        lines += [row_line(row, speed) for row in report['web']]
        lines.append('')

    return '\n'.join(lines)


def row_line(row, unit):
    name = row['name'].replace('Flyback.Core.Benchmarks.', '')

    if 'base' not in row:
        return f"| {name} | | | | | | {row['verdict']} |"

    allocated = row.get('allocated')
    extra = f"{allocated[0]} → {allocated[1]} B" if allocated else f"{row['samples'][0]} / {row['samples'][1]}"
    return f"| {name} | {unit(row['base'])} | {unit(row['head'])} | {row['change']:+.1f}% | {row['p']:.3f} | {extra} | {row['verdict']} |"


def time(ns):
    for scale, suffix in ((1e9, 's'), (1e6, 'ms'), (1e3, 'µs')):
        if ns >= scale:
            return f'{ns / scale:.3g} {suffix}'
    return f'{ns:.3g} ns'


def speed(times):
    return f'{times:.2f}x'


if __name__ == '__main__':
    main()
