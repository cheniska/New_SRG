#!/usr/bin/env python3
"""Build/check the lore catalog, validate Markdown articles and retrieve sections.

Standard library only. The supported YAML front matter is deliberately restricted
flat key/string metadata, with JSON-quoted string values (valid YAML).
"""
import argparse
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
DOCS = ROOT / 'GameDoc'
SCHEMA_PATH = DOCS / 'schemas/race-1.json'
CATALOG_PATH = DOCS / 'catalog.json'
SLOT = re.compile(r'^([IVX]+(?:-А)?(?:\.\d+)*)(?:[–—]([IVX]+(?:\.\d+)*))?\.\s+')
ANCHOR = re.compile(r'^<a id="([a-z][a-z0-9.-]*)"></a>$')
STATUS = re.compile(r'^<!-- section-status: ([a-z_]+) -->$')
ALIAS_PATH = DOCS / 'schemas/document-aliases.json'


def metadata(text):
    if not text.startswith('---\n'):
        return {}, 0
    lines = text.splitlines()
    data = {}
    for i, line in enumerate(lines[1:], 1):
        if line == '---':
            return data, i + 1
        match = re.fullmatch(r'([a-z_]+): (".*")', line)
        if not match:
            raise ValueError(f'front matter line {i + 1}: expected key: "string"')
        key, value = match.groups()
        if key in data:
            raise ValueError(f'duplicate metadata key: {key}')
        data[key] = json.loads(value)
        if not isinstance(data[key], str):
            raise ValueError(f'{key}: expected string')
    raise ValueError('unterminated front matter')


def expand_slots(start, end):
    if not end:
        return [start]
    roman = ['I', 'II', 'III', 'IV', 'V', 'VI', 'VII', 'VIII', 'IX',
             'X', 'XI', 'XII', 'XIII', 'XIV', 'XV', 'XVI', 'XVII']
    if start in roman and end in roman:
        return roman[roman.index(start):roman.index(end) + 1]
    if '.' in start and '.' in end:
        prefix, first = start.rsplit('.', 1)
        other, last = end.rsplit('.', 1)
        if prefix == other:
            return [f'{prefix}.{i}' for i in range(int(first), int(last) + 1)]
    raise ValueError(f'unsupported section range: {start}–{end}')


def parse_sections(text, markdown=False, schema=None):
    """Return source locations; section bounds include their nested sections."""
    schema = schema or json.loads(SCHEMA_PATH.read_text(encoding='utf-8'))
    lines = text.splitlines()
    sections = []
    _, offset = metadata(text) if markdown else ({}, 0)
    for i, line in enumerate(lines[offset:], offset):
        if markdown:
            match = re.match(r'^(#{2,6}) (.+)$', line)
            if not match:
                continue
            marks, title = match.groups()
            level = len(marks)
            marker = i - 1
            while marker >= offset and not lines[marker].strip():
                marker -= 1
            status = STATUS.fullmatch(lines[marker]) if marker >= offset else None
            anchor = ANCHOR.fullmatch(lines[marker - 1]) if marker > offset else None
            block_line = marker if anchor else i + 1
            sid = anchor[1] if anchor else None
            state = status[1] if status else None
        else:
            title = line
            level = 2
            sid = None
            state = None
            block_line = i + 1
        slot = SLOT.match(title)
        visual = title.upper().startswith('ПРИЛОЖЕНИЕ В')
        if not markdown and not (slot or visual):
            continue
        covers = expand_slots(*slot.groups()) if slot else []
        if slot and slot[1] == 'III-А':
            covers = list(schema['alternatives']['III']['covers'])
            if markdown and re.search(r'^## III\. ', text, re.M):
                covers.remove('III')
        if not markdown:
            if slot:
                sid = ('group.' + re.sub(r'[^a-z0-9]+', '-', title[:slot.end()].lower()).strip('-')
                       if slot[2] else schema['fixed_ids'].get(slot[1], 'slot.' + slot[1]))
                level = min(4, 2 + slot[1].count('.'))
            else:
                sid = 'visual'
        sections.append({'id': sid, 'title': title, 'slots': covers,
                         'status': state, 'level': level, 'line': i + 1,
                         'end_line': len(lines), 'block_line': block_line})
    for i, section in enumerate(sections):
        for following in sections[i + 1:]:
            if following['level'] <= section['level']:
                # Exclude the next Markdown anchor/status lines from this section.
                section['end_line'] = following['block_line'] - 1
                break
    return sections


def validate_article(path, schema=None):
    schema = schema or json.loads(SCHEMA_PATH.read_text(encoding='utf-8'))
    errors = []
    text = path.read_text(encoding='utf-8')
    try:
        meta, offset = metadata(text)
        sections = parse_sections(text, True, schema)
    except (ValueError, KeyError) as exc:
        return [str(exc)]
    if set(meta) != set(schema['metadata']):
        errors.append('metadata keys must exactly match the schema')
    for key, rule in schema['metadata'].items():
        value = meta.get(key, '')
        if 'enum' in rule and value not in rule['enum']:
            errors.append(f'{key}: unsupported value {value!r}')
        if 'pattern' in rule and not re.fullmatch(rule['pattern'], value):
            errors.append(f'{key}: invalid identifier {value!r}')
        if rule.get('nonempty') and not value.strip():
            errors.append(f'{key}: empty value')
    lines = text.splitlines()
    titles = [line[2:] for line in lines[offset:] if line.startswith('# ')]
    if titles != [meta.get('title')]:
        errors.append('exactly one H1 matching metadata.title is required')
    ids = [s['id'] for s in sections]
    if None in ids or len(ids) != len(set(ids)):
        errors.append('every section must have a unique explicit anchor')
    if any(s['level'] > schema['max_heading_depth'] for s in sections):
        errors.append('heading depth exceeds schema limit')
    slots = [slot for s in sections for slot in s['slots']]
    for slot in schema['required_slots']:
        if slot not in slots:
            errors.append(f'missing section slot: {slot}')
    if len(slots) != len(set(slots)):
        errors.append('section slots overlap or are duplicated')
    for s in sections:
        if s['status'] not in schema['section_statuses']:
            errors.append(f'{s["title"]}: missing/invalid section status')
        label_match = SLOT.match(s['title'])
        label = label_match[1] if label_match else None
        if label_match and not label_match[2] and label in schema['fixed_ids']:
            expected = schema['fixed_ids'][label]
            if s['id'] != expected:
                errors.append(f'{s["title"]}: expected stable id {expected}')
        expected_level = min(4, 2 + s['slots'][0].count('.')) if s['slots'] else s['level']
        if label and label.startswith('III-А'):
            expected_level = min(4, (3 if re.search(r'^## III\. ', text, re.M) else 2) + label.count('.'))
        if s['slots'] and s['level'] != expected_level:
            errors.append(f'{s["title"]}: incorrect heading level')
        content = lines[s['line']:s['end_line']]
        if s['status'] != 'filled' and not any(l.strip() and not l.startswith(('<', '#')) for l in content):
            errors.append(f'{s["title"]}: explain the section status in prose')
    previous_level = 1
    for s in sections:
        if s['level'] > previous_level + 1:
            errors.append(f'{s["title"]}: skipped heading level')
        previous_level = s['level']
    anchors = set(ids)
    for destination in re.findall(r'\]\(([^)]+)\)', text):
        filepart, _, fragment = destination.partition('#')
        if re.match(r'^\w+://', destination):
            continue
        target = path.parent / filepart if filepart else path
        if not target.exists():
            errors.append(f'broken file link: {destination}')
        elif fragment and target == path and fragment not in anchors:
            errors.append(f'broken section link: {destination}')
    if re.search(r'^\s*(?:<<<<<<< |>>>>>>> )', text, re.M):
        errors.append('unresolved merge marker')
    return errors


def legacy_identity(path):
    name = path.stem
    if re.match(r'^(big_race_|small_race_|also_races_)', name) and 'gameplay' not in name:
        name = re.sub(r'^(?:big_race|small_race|also_races)_(?:\d+|outer)_', '', name)
        return 'race.' + re.sub(r'_v\d+$', '', name), 'race'
    if name.startswith('common_'):
        return 'reference.' + name[7:], 'reference'
    if name.startswith('personage_'):
        return 'character.' + re.sub(r'_v\d+$', '', name[10:]), 'character'
    if name.startswith('planetologiya_'):
        return 'world.' + name[14:], 'world'
    return 'document.' + re.sub(r'[^a-z0-9]+', '-', name.lower()).strip('-'), 'document'


def build_catalog():
    """Catalog current root documents only; archive/editorial are deliberately excluded."""
    docs = []
    used = set()
    aliases = json.loads(ALIAS_PATH.read_text(encoding='utf-8'))
    old_paths = [p for paths in aliases.values() for p in paths]
    if len(old_paths) != len(set(old_paths)):
        raise ValueError('duplicate document aliases')
    for path in sorted(DOCS.iterdir()):
        if not path.is_file() or path.suffix not in ('.txt', '.md') or path.name in ('README.md', 'FORMAT.md', 'AGENTS.md'):
            continue
        text = path.read_text(encoding='utf-8')
        meta, _ = metadata(text)
        modern = bool(meta)
        did, kind = (meta['id'], meta['type']) if modern else legacy_identity(path)
        if did in used:
            raise ValueError(f'duplicate document id: {did}')
        used.add(did)
        title = meta['title'] if modern else next((line for line in text.splitlines()
                if re.search(r'[А-Яа-яA-Za-z]', line)), path.stem)
        docs.append({'id': did, 'path': path.relative_to(ROOT).as_posix(), 'type': kind,
                     'title': title, 'status': meta.get('status', 'canonical'),
                     'format': 'markdown' if modern else 'legacy-text',
                     'schema': meta.get('schema'), 'aliases': aliases.get(did, []),
                     'sections': parse_sections(text, modern)})
    if set(aliases) - used:
        raise ValueError('aliases reference an unknown document id')
    if any((ROOT / p).exists() for p in old_paths):
        raise ValueError('an alias still exists as a file; do not duplicate current documents')
    return {'catalog_version': 1, 'generated_by': 'python tools/docs/lore_format.py index',
            'scope': 'Current GameDoc root articles; excludes archive, editorial, concepts and templates.',
            'documents': docs}


def select_sections(doc, address):
    """Prefer an exact section over a broader legacy range containing it."""
    direct = [s for s in doc['sections'] if s['id'] == address]
    if direct:
        return direct
    schema = json.loads(SCHEMA_PATH.read_text(encoding='utf-8'))
    slot = next((slot for slot, sid in schema['fixed_ids'].items() if sid == address), address)
    single = [s for s in doc['sections'] if s['slots'] == [slot]]
    return single or [s for s in doc['sections'] if slot in s['slots']]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    index = sub.add_parser('index', help='Generate catalog.json from the documents')
    index.add_argument('--check', action='store_true', help='Fail if the saved catalog is stale')
    sub.add_parser('validate', help='Validate all articles using race-1 metadata')
    section = sub.add_parser('section', help='Print one section by document-id#section-id')
    section.add_argument('address')
    search = sub.add_parser('search', help='Search current articles with optional section scope')
    search.add_argument('query')
    search.add_argument('--section', help='Stable section ID or fixed slot, e.g. biology.reproduction or III.12')
    args = parser.parse_args()
    try:
        if args.command == 'validate':
            errors = []
            count = 0
            for path in DOCS.glob('*.md'):
                if metadata(path.read_text(encoding='utf-8'))[0].get('schema') == 'race-1':
                    count += 1
                    errors.extend(f'{path.name}: {error}' for error in validate_article(path))
            for error in errors:
                print('ERROR:', error)
            if not errors:
                print(f'OK: {count} Markdown article(s) validated against race-1.')
            return int(bool(errors))
        catalog = build_catalog()
        if args.command == 'index':
            rendered = json.dumps(catalog, ensure_ascii=False, indent=2) + '\n'
            if args.check:
                if not CATALOG_PATH.exists() or CATALOG_PATH.read_text(encoding='utf-8') != rendered:
                    print('ERROR: catalog.json is stale; run the index command')
                    return 1
                print(f'OK: catalog matches {len(catalog["documents"])} current documents.')
            else:
                CATALOG_PATH.write_text(rendered, encoding='utf-8')
                print(f'Generated catalog for {len(catalog["documents"])} documents.')
            return 0
        if args.command == 'section':
            did, separator, sid = args.address.partition('#')
            if not separator:
                raise ValueError('expected document-id#section-id')
            doc = next((d for d in catalog['documents'] if d['id'] == did or did in d['aliases']), None)
            if not doc:
                raise ValueError(f'unknown document: {did}')
            matches = select_sections(doc, sid)
            s = matches[0] if matches else None
            if not s:
                raise ValueError(f'unknown section: {sid}')
            lines = (ROOT / doc['path']).read_text(encoding='utf-8').splitlines()
            print(f'{doc["path"]}:{s["line"]} [{doc["id"]}#{s["id"]}]')
            print('\n'.join(lines[s['line'] - 1:s['end_line']]))
            return 0
        for doc in catalog['documents']:
            lines = (ROOT / doc['path']).read_text(encoding='utf-8').splitlines()
            scopes = select_sections(doc, args.section) if args.section else []
            if args.section and not scopes:
                continue
            for number, line in enumerate(lines, 1):
                if scopes and not any(s['line'] <= number <= s['end_line'] for s in scopes):
                    continue
                if args.query.casefold() in line.casefold():
                    print(f'{doc["path"]}:{number}: {line}')
        return 0
    except (ValueError, KeyError, OSError) as exc:
        print(f'ERROR: {exc}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
