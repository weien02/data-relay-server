#!/usr/bin/env python3
"""Helpers for run.sh, and the summary that fills the report's tables.

    experiments.py sample PID OUT.csv      ps sampler, one row per second until PID exits
    experiments.py summarise [RESULTS]     medians for Tables 4.6, 4.7, 4.8 and E4

Only the standard library is used.
"""
import csv
import glob
import json
import os
import re
import statistics
import subprocess
import sys
import time

HEADER_BYTES = 28       # IPv4 (20) + UDP (8), as a packet capture counts them
ETHERNET_BYTES = 14     # not known whether the references count it, so reported separately
CORES = os.cpu_count()


# ---------------------------------------------------------------- sampling

def parse_cpu_time(text):
    """ps time is [[dd-]hh:]mm:ss.cc; returns seconds."""
    days = 0
    if '-' in text:
        d, text = text.split('-', 1)
        days = int(d)
    seconds = 0.0
    for part in text.split(':'):
        seconds = seconds * 60 + float(part)
    return days * 86400 + seconds


def sample(pid, out_path):
    with open(out_path, 'w', newline='') as f:
        w = csv.writer(f)
        w.writerow(['unix_ms', 'cpu_seconds', 'rss_kb'])
        while True:
            r = subprocess.run(['ps', '-o', 'time=,rss=', '-p', str(pid)], capture_output=True, text=True)
            if r.returncode != 0 or not r.stdout.strip():
                return
            cpu, rss = r.stdout.split()
            w.writerow([int(time.time() * 1000), parse_cpu_time(cpu), int(rss)])
            f.flush()
            time.sleep(1.0)


# ---------------------------------------------------------------- one run

def load_run(json_path):
    base = json_path[:-len('.json')]
    with open(json_path) as f:
        report = json.load(f)
    with open(base + '.meta.json') as f:
        meta = json.load(f)
    samples = []
    if os.path.exists(base + '.ps.csv'):
        with open(base + '.ps.csv') as f:
            samples = [(int(r['unix_ms']), float(r['cpu_seconds']), int(r['rss_kb'])) for r in csv.DictReader(f)]
    meta['osDrops'] = int(open(base + '.osdrops').read()) if os.path.exists(base + '.osdrops') else None
    return meta, report, samples


def read_samples(base):
    with open(base + '.ps.csv') as f:
        return [(int(r['unix_ms']), float(r['cpu_seconds']), int(r['rss_kb'])) for r in csv.DictReader(f)]


def cpu_over(samples, start_ms, end_ms):
    """Percent of one core, and peak resident MB, between two wall clock times."""
    inside = [s for s in samples if start_ms <= s[0] <= end_ms]
    if len(inside) < 2:
        return None, None
    first, last = inside[0], inside[-1]
    return 100.0 * (last[1] - first[1]) / ((last[0] - first[0]) / 1000.0), max(s[2] for s in inside) / 1024.0


def measure(meta, report, samples):
    w = report['loadWindow']
    secs = w['seconds']
    m = {}
    # Server CPU over the load window: change in total CPU time over change in wall time.
    cpu, rss = cpu_over(samples, w['startUnixMs'], w['endUnixMs'])
    if cpu is not None:
        m['cpu_core_pct'], m['cpu_machine_pct'], m['rss_mb'] = cpu, cpu / CORES, rss
    m['os_drops'] = meta['osDrops']
    # Server outbound is what the clients received; inbound is what they sent.
    m['out_msg_s'] = w['datagramsReceived'] / secs
    m['in_msg_s'] = w['datagramsSent'] / secs
    wire_in = w['bytesReceived'] + HEADER_BYTES * w['datagramsReceived']
    wire_out = w['bytesSent'] + HEADER_BYTES * w['datagramsSent']
    m['out_kB_s'] = wire_in / secs / 1000.0
    m['kib_s_per_client'] = (wire_in + wire_out) / meta['clients'] / secs / 1024.0
    m['kib_s_per_client_payload'] = (w['bytesReceived'] + w['bytesSent']) / meta['clients'] / secs / 1024.0
    if w['entityDataReceived']:
        m['bytes_per_entity_update'] = wire_in / w['entityDataReceived']
        m['bytes_per_entity_update_eth'] = (wire_in + ETHERNET_BYTES * w['datagramsReceived']) / w['entityDataReceived']
    observed = w['framesForOneClient'] / secs
    m['observed_rate'] = observed
    m['retained_pct'] = 100.0 * observed / meta['rate']
    # Valid only if the harness kept up its publish rate, i.e. the harness was not the limit.
    expected_updates = meta['clients'] * w['framesForOneClient'] / meta['publishEvery']
    m['publish_ratio'] = w['stateUpdatesSent'] / expected_updates if expected_updates else 0
    m['valid'] = m['publish_ratio'] >= 0.95
    # Correctness and recovery.
    lag = report['lag']
    m['mean_lag'] = lag['sum'] / lag['samples'] if lag['samples'] else 0
    va = report.get('viewAge')
    if va and va['samples']:
        m['view_age_mean'] = va['sum'] / va['samples']
        m['view_age_max'] = va['max']
    m['gaps'] = report['syncFrames']['gapCount']
    m['stale_forever'] = report['crossCheck']['staleForever']
    m['respawn_lost'] = report['crossCheck']['respawnLost']
    f = report['liveFaults']
    m['impossible'] = f['impossibleStates']
    m['misattributed'] = f['misattributedStates']
    m['out_of_order'] = f['outOfOrderUpdates']
    episodes = report['reconnection']['episodes']
    if episodes:
        m['episodes'] = len(episodes)
        m['recovered'] = sum(1 for e in episodes if e['recovered'])
        times = [e['recoveryMs'] for e in episodes if e['recoveryMs'] is not None]
        rtts = [e['snapshotRoundTripMs'] for e in episodes if e['snapshotRoundTripMs'] is not None]
        m['recovery_ms'] = statistics.median(times) if times else None
        m['snapshot_rtt_ms'] = statistics.median(rtts) if rtts else None
        m['recovery_list'], m['rtt_list'] = times, rtts
    return m


# ---------------------------------------------------------------- summary

def group(results_dir, experiment):
    """name -> (meta of first repeat, list of measures, one per repeat)."""
    out = {}
    for path in sorted(glob.glob(os.path.join(results_dir, experiment, '*.json'))):
        if path.endswith('.meta.json') or path.endswith('.idle.json'):
            continue
        meta, report, samples = load_run(path)
        entry = out.setdefault(meta['name'], (meta, []))
        entry[1].append(measure(meta, report, samples))
    return out


def idle(results_dir, experiment):
    """Median CPU (% of one core) and memory of the server running with no clients."""
    cpus, rsss = [], []
    for path in glob.glob(os.path.join(results_dir, experiment, '*.idle.json')):
        with open(path) as f:
            w = json.load(f)
        cpu, rss = cpu_over(read_samples(path[:-len('.idle.json')]), w['startUnixMs'], w['endUnixMs'])
        if cpu is not None:
            cpus.append(cpu); rsss.append(rss)
    return (statistics.median(cpus), statistics.median(rsss)) if cpus else (None, None)


def med(runs, key, valid_only=True):
    values = [r[key] for r in runs if key in r and r[key] is not None and (r['valid'] or not valid_only)]
    return statistics.median(values) if values else None


def medr(runs, key, digits=0):
    """Median with the min-max range across repeats, as the report shows it."""
    values = [r[key] for r in runs if r.get(key) is not None]
    if not values:
        return 'n/a'
    m = fmt(statistics.median(values), digits)
    lo, hi = fmt(min(values), digits), fmt(max(values), digits)
    return m if lo == hi else '%s (%s-%s)' % (m, lo, hi)


def fmt(v, digits=1):
    if v is None:
        return 'n/a'
    return '{:,.{d}f}'.format(v, d=digits)


def invalid_note(runs):
    bad = sum(1 for r in runs if not r['valid'])
    return '' if not bad else '  (%d of %d runs harness-limited, excluded)' % (bad, len(runs))


def summarise(results_dir):
    print('machine: %d cores; medians of valid repeats\n' % CORES)

    g = group(results_dir, 'e1e2')
    if g:
        print('Table 4.6  E1, server resource use, 5 clients, 30 Hz (dirty-only, publish every frame)')
        meta, runs = g.get('c5_r30_dirty_pe1', (None, []))
        print('  CPU %% of whole machine : %s%%' % fmt(med(runs, 'cpu_machine_pct'), 2))
        print('  CPU %% of one core      : %s%%' % fmt(med(runs, 'cpu_core_pct'), 2))
        print('  memory                 : %s MB%s' % (fmt(med(runs, 'rss_mb')), invalid_note(runs)))
        floor, floor_rss = idle(results_dir, 'e1e2')
        print('  no clients (floor)     : %s%% of whole machine, %s%% of one core, %s MB\n' % (
            fmt(floor / CORES if floor is not None else None, 2), fmt(floor, 2), fmt(floor_rss)))
        print('Table 4.7  E2, bandwidth per client incl. IP+UDP headers (KiB/s)')
        for pe, label in ((1, 'every frame'), (10, 'every 10th frame')):
            for key, how in (('kib_s_per_client_payload', 'payload only'), ('kib_s_per_client', 'with headers')):
                d = med(g.get('c5_r30_dirty_pe%d' % pe, (None, []))[1], key)
                f = med(g.get('c5_r30_full_pe%d' % pe, (None, []))[1], key)
                saving = 100.0 * (f - d) / f if d is not None and f else None
                print('  %-17s %-13s dirty %s  full %s  saving %s%%' % (label, how, fmt(d, 2), fmt(f, 2), fmt(saving, 0)))
        print()

    g = group(results_dir, 'e3')
    if g:
        print('Table 4.8  E3, scaling at 20 Hz')
        print('  %7s %10s %10s %9s %8s %10s %9s %9s %8s' % ('clients', 'pred msg/s', 'meas msg/s', 'delivered', 'B/update',
                                                         'CPU %core', 'mem MB', 'retained', 'OS drops'))
        floor, floor_rss = idle(results_dir, 'e3')
        print('  %7d %10s %10s %9s %8s %10s %9s %9s %8s' % (0, '0', 'n/a', 'n/a', 'n/a', fmt(floor), fmt(floor_rss), 'n/a', 'n/a'))
        per_update_80 = None
        for name, (meta, runs) in sorted(g.items(), key=lambda kv: kv[1][0]['clients']):
            n = meta['clients']
            predicted = 20 * (n + n * n)
            measured = med(runs, 'out_msg_s')
            delivered = 100.0 * measured / predicted if measured is not None else None
            print('  %7d %10s %10s %8s%% %8s %10s %9s %8s%% %8s%s' % (
                n, fmt(predicted, 0), fmt(measured, 0), fmt(delivered), fmt(med(runs, 'bytes_per_entity_update')),
                fmt(med(runs, 'cpu_core_pct')), fmt(med(runs, 'rss_mb')), fmt(med(runs, 'retained_pct')),
                fmt(med(runs, 'os_drops', False), 0), invalid_note(runs)))
            if n == 80:
                per_update_80 = (med(runs, 'bytes_per_entity_update'), med(runs, 'bytes_per_entity_update_eth'))
        if per_update_80 and per_update_80[0]:
            ip, eth = per_update_80
            print('\n  StinkySteak scenario, 500 objects x 20 Hz, one observer (Table 4.8 bullets):')
            print('    IP layer        %s kB/s' % fmt(ip * 500 * 20 / 1000.0))
            print('    with Ethernet   %s kB/s' % fmt(eth * 500 * 20 / 1000.0))
        print()

    g = group(results_dir, 'e4')
    if g:
        print('E4  correctness, 20 clients, 20 Hz (Tables 4.9 and 4.10); median (min-max), 380 pairs per run')
        print('  %-26s %-14s %-11s %-18s %-9s %-18s %-10s' % ('config', 'stale forever', 'respawn', 'view age mean', 'worst', 'impossible/misattr/ooo', 'gaps'))
        for name, (meta, runs) in sorted(g.items()):
            if 'blackout' in name:
                continue
            print('  %-26s %-14s %-11s %-18s %-9s %-18s %-10s' % (
                name, medr(runs, 'stale_forever'), medr(runs, 'respawn_lost'), medr(runs, 'view_age_mean', 2),
                fmt(max(r['view_age_max'] for r in runs), 0),
                '%s/%s/%s' % (fmt(med(runs, 'impossible', False), 0), fmt(med(runs, 'misattributed', False), 0), fmt(med(runs, 'out_of_order', False), 0)),
                medr(runs, 'gaps')))
        for name, (meta, runs) in sorted(g.items()):
            if not any('episodes' in r for r in runs):
                continue
            total = sum(r.get('episodes', 0) for r in runs)
            recovered = sum(r.get('recovered', 0) for r in runs)
            # Pooled over every episode: a run has only a handful, so a median of medians would mislead.
            times = [t for r in runs for t in r.get('recovery_list', [])]
            rtts = [t for r in runs for t in r.get('rtt_list', [])]
            print('  %s: %d of %d episodes recovered; return-to-correct median %s ms (%s-%s), request-to-response %s ms' % (
                name, recovered, total, fmt(statistics.median(times), 0), fmt(min(times), 0), fmt(max(times), 0),
                fmt(statistics.median(rtts), 1)))
        print()

    sweep_dir = os.path.join(results_dir, 'sweep')
    if os.path.isdir(sweep_dir):
        print('Sync-rate sweep (Section 3.2 and E1): configured rate, observed rate, no-client CPU')
        for path in sorted(glob.glob(os.path.join(sweep_dir, 'idle_r*_rep1.idle.json')), key=lambda p: int(re.search(r'idle_r(\d+)_', p).group(1))):
            rate = int(re.search(r'idle_r(\d+)_', path).group(1))
            with open(path) as f:
                w = json.load(f)
            cpu_pct, _ = cpu_over(read_samples(path[:-len('.idle.json')]), w['startUnixMs'], w['endUnixMs'])
            with open(os.path.join(sweep_dir, 'c2_r%d_dirty_pe1_rep1.json' % rate)) as f:
                lw = json.load(f)['loadWindow']
            observed = lw['framesForOneClient'] / lw['seconds']
            print('  %4d Hz  observed %8.2f  (%+.2f frames over %.0f s)  no-client CPU %5.1f%% of one core' % (
                rate, observed, lw['framesForOneClient'] - rate * lw['seconds'], lw['seconds'], cpu_pct))


if __name__ == '__main__':
    if len(sys.argv) >= 4 and sys.argv[1] == 'sample':
        sample(int(sys.argv[2]), sys.argv[3])
    elif len(sys.argv) >= 2 and sys.argv[1] == 'summarise':
        summarise(sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), 'results'))
    else:
        print(__doc__)
        sys.exit(2)
