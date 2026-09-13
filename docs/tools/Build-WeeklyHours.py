"""
Build a weekly active-hours series from a powercfg /batteryreport XML.

Source semantics (verified against WORKSTATION, report v?, Windows 26100):
  History/HistoryEntry covers a contiguous period [LocalStartDate, LocalEndDate).
  Older entries span ~1 week; the most recent ~2 weeks are single-day entries
  where StartDate == EndDate.

  ActiveAcTime / ActiveDcTime = system in the working state (screen on).
  CsAcTime / CsDcTime         = connected standby (screen off, machine awake).

  Active time is the working-hours proxy. It matches the sleep study's
  MonitorOn -> MonitorOff sessions exactly, because both derive from the same
  power ETW source.

Allocation:
  Multi-day entries are spread pro-rata across the ISO weeks they overlap.
  Any week receiving pro-rata hours is flagged 'estimated' so a partial week
  is never mistaken for a measured one.
"""
import xml.etree.ElementTree as ET
import re, csv, datetime as dt, collections, sys

SRC = sys.argv[1] if len(sys.argv) > 1 else '/mnt/user-data/uploads/battery-report.xml'
OUT = sys.argv[2] if len(sys.argv) > 2 else '/mnt/user-data/outputs/weekly-hours.csv'

_DUR = re.compile(r'^P(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$')

def hours(s):
    m = _DUR.match(s or 'PT0S')
    if not m:
        raise ValueError('unparseable duration: %r' % s)
    d, h, mi, se = [int(x or 0) for x in m.groups()]
    return d * 24 + h + mi / 60 + se / 3600

def day(s):
    return dt.datetime.strptime(s[:10], '%Y-%m-%d').date()

root = ET.parse(SRC).getroot()
ns = root.tag.split('}')[0] + '}' if '}' in root.tag else ''
info = root.find(ns + 'SystemInformation')
machine = info.find(ns + 'ComputerName').text if info is not None else '?'

entries = [e.attrib for e in root.find(ns + 'History')]

# week_key -> accumulator
weeks = collections.defaultdict(lambda: {
    'active': 0.0, 'cs': 0.0, 'days': set(), 'estimated': False,
    'daily_days': set(), 'suspect': False,
})

for e in entries:
    start, end = day(e['LocalStartDate']), day(e['LocalEndDate'])
    active = hours(e['ActiveAcTime']) + hours(e['ActiveDcTime'])
    cs = hours(e['CsAcTime']) + hours(e['CsDcTime'])
    span = (end - start).days

    if span <= 0:
        # Single-day entry: exact, no allocation needed.
        iy, iw, _ = start.isocalendar()
        w = weeks[(iy, iw)]
        w['active'] += active
        w['cs'] += cs
        w['days'].add(start)
        w['daily_days'].add(start)
        continue

    # Sanity: accounted time cannot exceed elapsed time. Where it does, the
    # source entry is unreliable (clock changes are the likely cause on this
    # machine) - flag rather than silently rescale.
    accounted = active + cs
    suspect = accounted > span * 24 * 1.02

    # Multi-day entry: spread pro-rata across covered days.
    per_day_active = active / span
    per_day_cs = cs / span
    for i in range(span):
        d = start + dt.timedelta(days=i)
        iy, iw, _ = d.isocalendar()
        w = weeks[(iy, iw)]
        w['active'] += per_day_active
        w['cs'] += per_day_cs
        w['days'].add(d)
        if suspect:
            w['suspect'] = True
        # Half-open period: the last covered day is end - 1.
        last = end - dt.timedelta(days=1)
        if start.isocalendar()[:2] != last.isocalendar()[:2]:
            w['estimated'] = True

rows = []
for (iy, iw) in sorted(weeks):
    w = weeks[(iy, iw)]
    monday = dt.date.fromisocalendar(iy, iw, 1)
    covered = len(w['days'])
    active = w['active']
    # Days on which any active time was recorded - only meaningful where we
    # have true daily entries; otherwise blank rather than guessed.
    if w['daily_days']:
        worked = sum(1 for d in sorted(w['daily_days']) if d.weekday() < 5)
        basis = 'daily'
    else:
        worked = ''
        basis = 'weekly'
    rows.append({
        'iso_week': '%d-W%02d' % (iy, iw),
        'week_start': monday.isoformat(),
        'week_end': (monday + dt.timedelta(days=6)).isoformat(),
        'active_hours': round(active, 2),
        'standby_hours': round(w['cs'], 2),
        'days_covered': covered,
        'hours_per_covered_day': round(active / covered, 2) if covered else '',
        'weekdays_with_data': worked,
        'basis': basis,
        'quality': ('suspect' if w['suspect'] else
                    'estimated' if w['estimated'] else
                    'partial' if covered < 7 else 'measured'),
    })

with open(OUT, 'w', newline='', encoding='utf-8') as f:
    wr = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
    wr.writeheader()
    wr.writerows(rows)

full = [r for r in rows if r['quality'] in ('measured', 'estimated') and r['days_covered'] == 7]
vals = sorted(r['active_hours'] for r in full)
print('machine        :', machine)
print('weeks written  :', len(rows))
print('range          :', rows[0]['week_start'], '->', rows[-1]['week_end'])
print('usable weeks   :', len(full))
print('suspect weeks  :', sum(1 for r in rows if r['quality'] == 'suspect'))
if vals:
    print('median active  : %.1f h' % vals[len(vals) // 2])
    print('mean active    : %.1f h' % (sum(vals) / len(vals)))
    print('p10 / p90      : %.1f / %.1f h' % (vals[int(len(vals) * .1)], vals[int(len(vals) * .9)]))
print('output         :', OUT)
