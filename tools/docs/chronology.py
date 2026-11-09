#!/usr/bin/env python3
"""Validate bound event dates; sync marked dates and generate the readable registry.

Only an explicit <!-- date:event.id --> binding authorizes synchronization.
Unmarked prose is never changed by year matching. All scope limits are reported.
"""
import argparse
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
DATE = r'\d{4}(?:[–…]\d{4})?'
BOUND = re.compile(r'(?<![\d–…])(?P<date>'+DATE+r')<!-- date:(?P<id>[a-z0-9.-]+) -->')
MARKER = re.compile(r'<!-- date:([^>]+) -->')
PRECISIONS = {'exact', 'by', 'approximate', 'range', 'approximate_range',
              'soon_after', 'after_peace', 'conventional_boundary'}


def load_events(root):
    data = json.loads((root/'GameDoc/data/chronology.json').read_text())
    events = {}
    for event in data['events']:
        key = event['id']
        if key in events:
            raise ValueError(f'Duplicate event: {key}')
        if not re.fullmatch(DATE, event['date']) or event['precision'] not in PRECISIONS:
            raise ValueError(f'Invalid date or precision: {key}')
        if '–' in event['date'] and event['date'].split('–')[0] > event['date'].split('–')[1]:
            raise ValueError(f'Reversed date range: {key}')
        events[key] = event
    return data, events


def readable(data):
    labels={'approximate':'около ', 'approximate_range':'примерно ', 'by':'к ',
            'soon_after':'вскоре после ', 'after_peace':'после мира ',
            'conventional_boundary':'условный рубеж '}
    lines=['---','id: "reference.chronology"','type: "reference"',
           'title: "Единый реестр дат эпохи контактов"','schema: "reference-1"',
           'status: "canonical"','---','','# Единый реестр дат эпохи контактов','',
           'Этот справочник автоматически собирается из `data/chronology.json`. '
           'Дата и степень её точности хранятся у события с постоянным ID. '
           'Связные расовые истории сохраняются в досье; общий ход — в '
           '[статье периода](reference_period_2500_2800.md).','',
           data['scope'],'', '<a id="events"></a>', '<!-- section-status: filled -->','',
           '## Датированные события','','| Дата | Событие | ID | Источник |', '|---|---|---|---|']
    for e in sorted(data['events'], key=lambda e:(int(e['date'][:4]), e['id'])):
        lines.append(f"| {labels.get(e['precision'],'')}{e['date']} | {e['title']} | `{e['id']}` | [Подробнее]({e['source']}) |")
    lines += ['', '<a id="undated"></a>', '<!-- section-status: needs_development -->','',
              '## События без точного года','', 'Точные годы следующих событий требуют разработки.','']
    for e in data['undated']:
        lines.append(f"- `{e['id']}` — {e['description']} [Источник]({e['source']}).")
    lines += ['', '<a id="maintenance"></a>', '<!-- section-status: filled -->','',
              '## Сопровождение','',
              'Согласованную дату меняют в JSON, затем выполняют `python tools/docs/chronology.py sync`. '
              'Команда обновляет только явно привязанные к событию даты и этот справочник. '
              'Проверка `python tools/docs/chronology.py check` отклоняет расхождение, неизвестный ID, '
              'повреждённую привязку, потерю всех привязок события и устаревший справочник. '
              'Комментарии привязок не видны в статье. Непривязанные даты и смысл формулировок '
              'требуют редакторской проверки: совпадение числа не означает совпадения события.','']
    return '\n'.join(lines)


def process(root, sync=False):
    data, events = load_events(root)
    errors=[]; counts={key:0 for key in events}
    for e in [*data['events'], *data['undated']]:
        file, anchor=e['source'].split('#',1)
        path=root/'GameDoc'/file
        if not path.exists() or f'<a id="{anchor}">' not in path.read_text():
            errors.append(f"Broken source: {e['id']}: {e['source']}")
    for path in sorted((root/'GameDoc').glob('*.md')):
        if path.name=='reference_chronology.md' or not path.name.startswith(('race_', 'reference_')): continue
        text=path.read_text(); updated=[]
        for line_no,line in enumerate(text.splitlines(True),1):
            matches=list(BOUND.finditer(line))
            if len(matches)!=len(MARKER.findall(line)):
                errors.append(f'{path.name}:{line_no}: malformed date binding')
            def replace(m):
                key=m['id']
                if key not in events:
                    errors.append(f'{path.name}:{line_no}: unknown event {key}')
                    return m[0]
                counts[key]+=1
                expected=events[key]['date']
                if m['date']!=expected and not sync:
                    errors.append(f'{path.name}:{line_no}: {key}: {m["date"]} != {expected}')
                return expected+f'<!-- date:{key} -->' if sync else m[0]
            updated.append(BOUND.sub(replace,line))
        replacement=''.join(updated)
        if sync and replacement!=text: path.write_text(replacement)
    for key,count in counts.items():
        if not count: errors.append(f'Unbound event: {key}')
    target=root/'GameDoc/reference_chronology.md'; generated=readable(data)
    if sync: target.write_text(generated)
    elif not target.exists() or target.read_text()!=generated:
        errors.append('Readable registry is stale: run chronology.py sync')
    return errors, counts


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('command',choices=['check','sync']);args=p.parse_args()
    try: errors,counts=process(ROOT,args.command=='sync')
    except (ValueError,KeyError) as exc: print(f'ERROR: {exc}'); return 1
    for error in errors: print('ERROR:',error)
    print(f'Chronology: {len(counts)} events; {sum(counts.values())} explicit bindings; {len(errors)} errors.')
    return bool(errors)


if __name__=='__main__': sys.exit(main())
