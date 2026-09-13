"""
Extract working hours from a powercfg /sleepstudy report (HTML with embedded XML).

Active time == screen on. On a work-only machine that is working time.

Extraction notes (verified against WORKSTATION):
  Active periods are split across two element types inside <ScenarioInstances>:
    RecentUsageInstance  Type="Active"  - LocalTimestamp + Duration
    OsStateInstance      Type="Active"  - LocalTimestamp + ExitLocalTimestamp
  Neither alone is complete. Taking both and merging overlapping intervals
  reproduces the 14-day JSON report's figures to the second.

  Duration here is in 100ns ticks. The JSON variant of the same report uses
  MICROSECONDS for the same field name. Do not share a constant between them.

  *Local* timestamps in the JSON variant carry a bogus trailing 'Z'.
  In this XML variant they do not. Parse as naive local either way.

Outputs:
  daily-hours.csv   one row per day with recorded activity
  weekly-hours-sleepstudy.csv  ISO-week rollup of the same
"""
import re, csv, sys, collections, datetime as dt

SRC = sys.argv[1] if len(sys.argv) > 1 else '/mnt/user-data/uploads/sleepstudy28.html'
OUTDIR = sys.argv[2] if len(sys.argv) > 2 else '/mnt/user-data/outputs'

# A short isolated session hours away from the rest is a glance at the machine,
# not a work block. Counted in totals, but excluded when deciding start/finish.
FRINGE_MAX_MIN = 5
FRINGE_GAP_MIN = 90

raw = open(SRC, encoding='utf-8', errors='replace').read()
i, j = raw.find('<ScenarioInstances>'), raw.find('</ScenarioInstances>')
if i < 0:
    sys.exit('no <ScenarioInstances> block found - is this a sleepstudy report?')
blk = raw[i:j]

def elements(tag):
    return [dict(re.findall(r'(\w+)="([^"]*)"', m))
            for m in re.findall(r'<%s\s(.*?)>' % tag, blk, re.S)]

def ts(s):
    return dt.datetime.strptime(s, '%Y-%m-%dT%H:%M:%S')

intervals = []
for a in elements('RecentUsageInstance'):
    if a.get('Type') != 'Active':
        continue
    start = ts(a['LocalTimestamp'])
    intervals.append((start, start + dt.timedelta(seconds=float(a['Duration']) / 1e7)))
for a in elements('OsStateInstance'):
    if a.get('Type') != 'Active':
        continue
    intervals.append((ts(a['LocalTimestamp']), ts(a['ExitLocalTimestamp'])))

intervals.sort()
merged = []
for start, end in intervals:
    if merged and start <= merged[-1][1]:
        merged[-1][1] = max(merged[-1][1], end)
    else:
        merged.append([start, end])

# Sleep/hibernate/shutdown transitions, for context on each day.
transitions = collections.defaultdict(list)
for a in elements('OsStateInstance'):
    if a.get('Type') in ('Sleep', 'Hibernate', 'Shutdown', 'Shutdown (Hybrid)'):
        transitions[ts(a['LocalTimestamp']).date()].append(a['Type'])

by_day = collections.defaultdict(list)
for start, end in merged:
    by_day[start.date()].append((start, end))

daily = []
for d in sorted(by_day):
    sessions = sorted(by_day[d])
    total = sum((e - s).total_seconds() for s, e in sessions) / 3600

    # Trim fringe sessions before computing start/finish.
    core = list(sessions)
    while len(core) > 1:
        s, e = core[-1]
        if (e - s).total_seconds() / 60 <= FRINGE_MAX_MIN and \
           (s - core[-2][1]).total_seconds() / 60 >= FRINGE_GAP_MIN:
            core.pop()
        else:
            break
    while len(core) > 1:
        s, e = core[0]
        if (e - s).total_seconds() / 60 <= FRINGE_MAX_MIN and \
           (core[1][0] - e).total_seconds() / 60 >= FRINGE_GAP_MIN:
            core.pop(0)
        else:
            break

    first, last = core[0][0], core[-1][1]
    span = (last - first).total_seconds() / 3600
    core_total = sum((e - s).total_seconds() for s, e in core) / 3600
    gaps = [(core[k + 1][0] - core[k][1]).total_seconds() / 60 for k in range(len(core) - 1)]

    daily.append({
        'date': d.isoformat(),
        'weekday': d.strftime('%a'),
        'first_active': first.strftime('%H:%M'),
        'last_active': last.strftime('%H:%M'),
        'span_hours': round(span, 2),
        'active_hours': round(total, 2),
        'away_hours': round(span - core_total, 2),
        'sessions': len(sessions),
        'longest_break_min': round(max(gaps), 0) if gaps else 0,
        'fringe_sessions': len(sessions) - len(core),
        'transitions': '|'.join(sorted(set(transitions.get(d, [])))),
    })

weeks = collections.defaultdict(lambda: {'active': 0.0, 'days': 0, 'span': 0.0})
for r in daily:
    d = dt.date.fromisoformat(r['date'])
    iy, iw, _ = d.isocalendar()
    w = weeks[(iy, iw)]
    w['active'] += r['active_hours']
    w['span'] += r['span_hours']
    w['days'] += 1

weekly = []
for (iy, iw) in sorted(weeks):
    w = weeks[(iy, iw)]
    monday = dt.date.fromisocalendar(iy, iw, 1)
    weekly.append({
        'iso_week': '%d-W%02d' % (iy, iw),
        'week_start': monday.isoformat(),
        'active_hours': round(w['active'], 2),
        'days_with_activity': w['days'],
        'mean_hours_per_day': round(w['active'] / w['days'], 2),
        'total_span_hours': round(w['span'], 2),
        'source': 'sleepstudy',
    })

def write(name, rows):
    path = '%s/%s' % (OUTDIR, name)
    with open(path, 'w', newline='', encoding='utf-8') as f:
        wr = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        wr.writeheader()
        wr.writerows(rows)
    return path

p1 = write('daily-hours.csv', daily)
p2 = write('weekly-hours-sleepstudy.csv', weekly)

work = [r for r in daily if dt.date.fromisoformat(r['date']).weekday() < 5]
vals = sorted(r['active_hours'] for r in work)
print('days extracted   :', len(daily), '(%d weekdays)' % len(work))
print('range            :', daily[0]['date'], '->', daily[-1]['date'])
if vals:
    print('weekday active   : median %.2f h, mean %.2f h, min %.2f, max %.2f'
          % (vals[len(vals) // 2], sum(vals) / len(vals), vals[0], vals[-1]))
print('written          :', p1)
print('                  ', p2)
