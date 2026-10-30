#!/usr/bin/env python3
"""Check the current lore index, fixed section slots and local navigation links.

Run from any directory. No dependencies beyond Python's standard library.
This is an editorial structure check, not a semantic canon validator.
"""
from pathlib import Path
import re
import sys
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parents[2]
DOCS = ROOT / 'GameDoc'
ROMAN = ['I', 'II', 'III', 'IV', 'V', 'VI', 'VII', 'VIII', 'IX',
         'X', 'XI', 'XII', 'XIII', 'XIV', 'XV', 'XVI', 'XVII']
REQUIRED = ROMAN + ['I.1', 'II.1', 'II.2', 'II.3', 'II.4', 'VI.5'] + [
    f'X.{i}' for i in range(1, 18)
] + [f'XI.{i}' for i in range(1, 5)]
HEADER = re.compile(r'^([IVX]+(?:-А)?(?:\.\d+)*)(?:[–—]([IVX]+(?:\.\d+)*))?\.\s+')
RACE = re.compile(r'(?:big_race_|small_race_|also_races_).+\.txt$')


def covered_slots(text):
    slots = set()
    top_order = []
    for line in text.splitlines():
        match = HEADER.match(line)
        if not match:
            continue
        start, end = match.groups()
        slots.add(start)
        if end:
            slots.add(end)
            if start in ROMAN and end in ROMAN:
                expanded = ROMAN[ROMAN.index(start):ROMAN.index(end) + 1]
                slots.update(expanded)
                top_order.extend(expanded)
            elif '.' in start and '.' in end:
                prefix, first = start.rsplit('.', 1)
                other, last = end.rsplit('.', 1)
                if prefix == other:
                    slots.update(f'{prefix}.{i}' for i in range(int(first), int(last) + 1))
        elif start in ROMAN:
            top_order.append(start)
        if start == 'III-А':
            slots.add('III')
            # An alternative architecture section occupies the same top-level slot.
            if 'III' not in top_order:
                top_order.append('III')
    return slots, top_order


def main():
    errors = []
    index = DOCS / 'README.md'
    if not index.exists():
        print('ERROR: GameDoc/README.md is missing')
        return 1
    text = index.read_text(encoding='utf-8')
    listed = re.findall(r'\]\(([^)]+\.txt)\)', text)
    races = [p for p in DOCS.glob('*.txt') if RACE.fullmatch(p.name)
             and 'gameplay' not in p.name]
    families = {}
    for path in sorted(races):
        relative = path.relative_to(ROOT)
        family = re.sub(r'_v\d+(?=\.txt$)', '', path.name)
        if family in families:
            errors.append(f'{relative}: multiple current versions of {family}')
        families[family] = path
        if listed.count(path.name) != 1:
            errors.append(f'{relative}: must appear exactly once in the current index')
        body = path.read_text(encoding='utf-8')
        slots, order = covered_slots(body)
        missing = [slot for slot in REQUIRED if slot not in slots]
        if missing:
            errors.append(f'{relative}: missing slots: {", ".join(missing)}')
        if order != ROMAN:
            errors.append(f'{relative}: top-level sections out of order or duplicated: {order}')
        if 'ПРИЛОЖЕНИЕ В' not in body.upper():
            errors.append(f'{relative}: missing visual appendix')
        if re.search(r'^---\s*(?:[IVX]+(?:-А)?[.\s])', body, re.M):
            errors.append(f'{relative}: heading still has decorative dash wrappers')
        if re.search(r'^(?:<<<<<<< |>>>>>>> )', body, re.M):
            errors.append(f'{relative}: unresolved merge markers')
    for path in [*DOCS.rglob('README.md'), ROOT / 'docs/README.md']:
        body = path.read_text(encoding='utf-8')
        for target in re.findall(r'\]\(([^)]+)\)', body):
            if re.match(r'\w+://|mailto:', target) or target.startswith('#'):
                continue
            destination = unquote(target.split('#', 1)[0])
            if destination and not (path.parent / destination).exists():
                errors.append(f'{path.relative_to(ROOT)}: broken link: {target}')
    template = (DOCS / 'templates/Шаблон_статьи_о_расе_v3.md').read_text(encoding='utf-8')
    principles = template.split('ОБЩИЕ ПРИНЦИПЫ', 1)[1].split('КЛАССЫ РАС СОДРУЖЕСТВА', 1)[0]
    numbers = [int(n) for n in re.findall(r'^(\d+)\. ', principles, re.M)]
    if numbers != list(range(1, 12)):
        errors.append(f'template: invalid principle numbering: {numbers}')
    for error in errors:
        print(f'ERROR: {error}')
    if errors:
        return 1
    print(f'OK: {len(races)} current race articles; fixed slots, heading order, visual appendices, local index links and template numbering verified.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
