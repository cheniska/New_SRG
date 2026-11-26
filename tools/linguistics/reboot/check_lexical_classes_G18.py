#!/usr/bin/env python3
"""Limited POS regression checker for the working Ndzeraк lexicon.

Checks dictionary coverage (118 forms), the primary POS and predicate-slot
consistency of curated G18 examples. It is NOT a general grammar parser and
does not validate pronunciation, transitivity or every constituent position.
Run: python3 tools/linguistics/reboot/check_lexical_classes_G18.py
"""
from __future__ import annotations

import re
from pathlib import Path

DIR = Path(__file__).resolve().parent
FILE = DIR / "active_lexicon_register_G17_2026-11-26.md"
ENTRY = re.compile(r"^\|\s*\*\*(.+?)\*\*\s*\|\s*([^|]+?)\s*\|")

# (surface test, should final word be licensed as a verb predicate?)
SENTENCES = [
    ("Мцари цвэк дзрина", True),
    ("Дзэру цвэк тцу дзрина", True),
    ("Дзэру цвэк дзрина цо", True),
    ("Мцари нцор", True),
    ("Дзорон нцорок", True),
    ("Мцари цвэк мдзарок", True),
    ("Мцари цвэк вэтик дзрина", True),
    ("Мцари цвэк тэмар мдзар", True),
    ("Мцари ицэ цворэт мдзар", True),
    ("Мцари цзен цворэт мдзар", True),
    ("Цвэри цвэк дзрина", True),
    ("Мцари цвэра дзрина", True),
    ("Мцари мцив", False),
    ("Мцари пцак", False),
    ("Мцари цвирак", False),
    ("Мцари цвэк цвэк", False),
]

def lexicon() -> dict[str, str]:
    text = FILE.read_text(encoding="utf-8")
    text = text.split("## Отобранные смысловые корни C40 — 40", 1)[1]
    text = text.split("## Быстрая автоматизированная сверка", 1)[0]
    entries = {}
    for raw in text.splitlines():
        if match := ENTRY.match(raw):
            word, pos = match.group(1), match.group(2).strip()
            key = word.casefold()
            assert key not in entries, f"Duplicate lexeme: {word}"
            entries[key] = pos
    assert len(entries) == 118, f"Unexpected number of entries: {len(entries)}"
    assert set(entries.values()).isdisjoint({"N/V", "N/ADV", "ADV/N"}), (
        "Ambiguous historical POS labels remain in G17")
    assert entries["мцив"] == "N" and entries["нцор"] == "V"
    assert entries["пцак"] == "N"
    assert entries["вэтик"] == "N+ADV" and entries["тэмар"] == "V+ADV"
    return entries

def verb_surface(word: str, dictionary: dict[str, str]) -> bool:
    w = word.casefold()
    if dictionary.get(w) in {"V", "V+ADV"}:
        return True
    # Perfective suffix has -к after vowels, -ок after consonants;
    # this is a suffix check, not a complete transitivity/semantics parser.
    for form, pos in dictionary.items():
        if pos not in {"V", "V+ADV"}:
            continue
        suffix = "к" if form.endswith(("а", "э", "и", "о", "у", "е")) else "ок"
        if w == form + suffix:
            return True
    return False

def main() -> None:
    dictionary = lexicon()
    for line, expected in SENTENCES:
        tokens = line.casefold().split()
        if tokens[-1] == "цо":
            tokens.pop()
        result = verb_surface(tokens[-1], dictionary)
        assert result == expected, f"POS mismatch: {line}: got {result}"
    print(f"G18 PASS: {len(dictionary)} lexemes; {len(SENTENCES)} "
          "predicate-slot fixtures match editorial expectations")
    print("Warning: POS fixtures do not prove full syntax or acoustic contrast.")

if __name__ == "__main__":
    main()
