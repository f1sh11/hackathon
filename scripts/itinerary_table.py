#!/usr/bin/env python3
"""Extract a SydneyBuddy JSON result into a compact itinerary. Python 3.10+, no dependencies.

python3 scripts/itinerary_table.py result.json
python3 scripts/itinerary_table.py result.json --output itinerary.csv
python3 scripts/itinerary_table.py result.json --output itinerary.md --language en

Only final draft.days stops/legs are used, never the agent's proposed explanation.
Unknown transport and unallocated time are not invented.
"""
import argparse
import csv
import io
import json
import sys
from pathlib import Path

LABELS = {
    'zh': ['日期', '時間', '地點／活動', '前往方式'],
    'en': ['Date', 'Time', 'Place / activity', 'Getting there'],
}
MODES = {
    'walk': ('步行', 'Walk'), 'walking': ('步行', 'Walk'),
    'pedestrian': ('步行', 'Walk'), 'transit': ('公共運輸', 'Public transport'),
    'bus': ('公車', 'Bus'), 'train': ('火車', 'Train'), 'rail': ('鐵路', 'Rail'),
    'tram': ('輕軌', 'Tram'), 'light_rail': ('輕軌', 'Light rail'),
    'subway': ('地鐵', 'Metro'), 'metro': ('地鐵', 'Metro'),
    'ferry': ('渡輪', 'Ferry'), 'drive': ('開車', 'Drive'),
    'car': ('開車', 'Car'), 'taxi': ('計程車', 'Taxi'),
}

def wording(zh, en, language):
    return en if language == 'en' else zh

def clock(value, language):
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        value = int(value)
        return f'{value // 60:02d}:{value % 60:02d}'
    if isinstance(value, str) and value:
        # Keep the clock from an ISO timestamp; do not reinterpret its time zone.
        if 'T' in value:
            return value.split('T', 1)[1][:5]
        return value
    return wording('時間未提供', 'Time unavailable', language)

def location(point, language):
    if not isinstance(point, dict):
        return wording('地點未提供', 'Place unavailable', language)
    return (point.get('nameEn') if language == 'en' else None) or point.get('name') or wording('地點未提供', 'Place unavailable', language)

def transport(leg, profile, language):
    if not isinstance(leg, dict):
        return wording('交通未提供', 'Transport unavailable', language)
    parts = []
    steps = leg.get('steps') or []
    for step in steps:
        if not isinstance(step, dict):
            continue
        mode = str(step.get('mode') or '').lower()
        name = MODES.get(mode, (mode, mode))[language == 'en']
        line = step.get('line')
        if name:
            item = f'{name} {line}' if line else name
            if not parts or parts[-1] != item:
                parts.append(item)
    if parts:
        label = ' → '.join(parts)
    else:
        mode = str(leg.get('mode') or '').lower()
        # A walking-only profile explicitly identifies otherwise unlabeled route legs.
        if not mode and profile.get('transportPreference') == 'walk':
            mode = 'walk'
        label = MODES.get(mode, (mode, mode))[language == 'en'] if mode else wording('交通未提供', 'Transport unavailable', language)
    minutes = leg.get('minutes')
    if isinstance(minutes, (int, float)) and not isinstance(minutes, bool):
        label += wording(f'・約 {minutes:g} 分鐘', f' · about {minutes:g} min', language)
    source = str(leg.get('source') or '').lower()
    if any(term in source for term in ('估算', '演示', 'estimate', 'demo', '直线', '直線')):
        label += wording('（估算）', ' (estimate)', language)
    departure, arrival = leg.get('departureTime'), leg.get('arrivalTime')
    if departure and arrival:
        label += f' [{clock(departure, language)}–{clock(arrival, language)}]'
    return label

def extract(data, language='zh'):
    if not isinstance(data, dict):
        raise ValueError('JSON 根節點必須是物件。')
    draft = data.get('draft', data)
    if not isinstance(draft, dict) or not isinstance(draft.get('days'), list):
        raise ValueError('找不到 draft.days 或 days 行程清單；請提供行程結果 JSON。')
    profile = draft.get('profile') or {}
    rows = []
    for day in draft['days']:
        if not isinstance(day, dict):
            raise ValueError('days 的每一筆必須是物件。')
        date = day.get('date') or wording('日期未提供', 'Date unavailable', language)
        stops, legs = day.get('stops') or [], day.get('legs') or []
        start = day.get('start') or profile.get('start')
        if start:
            rows.append([date, clock(day.get('planningStartMinute', profile.get('startMinute')), language),
                         wording('出發：', 'Start: ', language) + location(start, language), '—'])
        for index, stop in enumerate(stops):
            if not isinstance(stop, dict):
                raise ValueError('stops 的每一筆必須是物件。')
            rows.append([date, f"{clock(stop.get('arrival'), language)}–{clock(stop.get('departure'), language)}",
                         location(stop, language), transport(legs[index] if index < len(legs) else None, profile, language)])
        end = day.get('end') or profile.get('end')
        if end:
            # Only consume the return leg if there is one extra leg after all stops.
            leg = legs[len(stops)] if len(legs) == len(stops) + 1 else None
            rows.append([date, clock(day.get('finishTime'), language),
                         wording('抵達終點：', 'Arrive at end: ', language) + location(end, language), transport(leg, profile, language)])
    return draft, rows

def markdown(draft, rows, language):
    def escape(value):
        return str(value).replace('|', '\\|').replace('\n', ' ')
    lines = ['| ' + ' | '.join(LABELS[language]) + ' |', '| --- | --- | --- | --- |']
    lines.extend('| ' + ' | '.join(map(escape, row)) + ' |' for row in rows)
    lines += ['', wording('交通欄是「從上一站前往本列地點」；活動時間取 arrival／departure。未列出的時段不視為已安排行程。',
                           'Transport describes travel from the previous location. Activity times use arrival/departure. Unlisted periods are not scheduled activities.', language)]
    if draft.get('recommendationStatus') == 'partial' or any(d.get('recommendationStatus') == 'partial' for d in draft['days'] if isinstance(d, dict)):
        lines += ['', wording('狀態：待補充草稿；請另查看原始 JSON 的 warnings。', 'Status: partial draft. Check warnings in the source JSON.', language)]
    return '\n'.join(lines) + '\n'

def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('input', type=Path, help='Postman 下載的行程結果 JSON（檔名不限）')
    parser.add_argument('-o', '--output', type=Path, help='輸出 .md 或 .csv；省略則顯示表格')
    parser.add_argument('--language', choices=['zh', 'en'], default='zh')
    args = parser.parse_args()
    try:
        data = json.loads(args.input.read_text(encoding='utf-8-sig'))
        draft, rows = extract(data, args.language)
        if args.output and args.output.suffix.lower() not in ('.csv', '.md'):
            raise ValueError('輸出副檔名請使用 .csv 或 .md。')
        if args.output and args.input.resolve() == args.output.resolve():
            raise ValueError('輸出不可覆蓋原始 JSON。')
        if args.output and args.output.suffix.lower() == '.csv':
            stream = io.StringIO(newline='')
            writer = csv.writer(stream)
            # Keep spreadsheet applications from interpreting imported place names as formulas.
            safe = lambda v: "'" + str(v) if str(v).lstrip().startswith(('=', '+', '-', '@')) else str(v)
            writer.writerow(LABELS[args.language])
            writer.writerows([[safe(value) for value in row] for row in rows])
            content = stream.getvalue()
            args.output.write_text(content, encoding='utf-8-sig')
        else:
            content = markdown(draft, rows, args.language)
            if args.output:
                args.output.write_text(content, encoding='utf-8')
            else:
                print(content, end='')
        if args.output:
            print(f'已輸出 {len(rows)} 列：{args.output}')
    except (OSError, ValueError, TypeError) as error:
        parser.exit(1, f'無法轉換：{error}\n')

if __name__ == '__main__':
    main()
