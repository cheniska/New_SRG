#!/usr/bin/env python3
"""Small reproducible G19 fixture check; no third-party dependencies.

Tests proposed structural distinction between bare noun phrases, verbal
predicates and a candidate overt nominal copula. These tests do not validate
real-world acoustic intelligibility or canonize the candidate words.
Run: python3 tools/linguistics/reboot/check_nonverbal_predicates_G19.py
"""
from __future__ import annotations

from pathlib import Path
import re

ROOT = Path(__file__).resolve().parent
G17 = ROOT / "active_lexicon_register_G17_2026-11-26.md"
ROW = re.compile(r"^\|\s*\*\*(.+?)\*\*\s*\|\s*([^|]+?)\s*\|")

COPULA = "лдзо"  # Author-selected G19/V2 copula spelling/function
EXPERIENCE = "мдзэкор"  # G19 trial, NOT yet approved
LICENSED_N_TO_V = {"мцив"}  # G19 suggested individual exception only
VOWELS = set("аэиоуе")

# (sentence, expect well-formed predicate head in this specific G19 model)
FIXTURES = [
    ("Ицэ Цраг Лдзо", True),
    ("Ицэ Цвирак Лдзо", True),
    ("Дзорон Эцан Лдзо", True),
    ("Цвэк Рацэн Лдзо", True),
    ("Цвэк Цвирак Лдзо", True),
    ("Ицэ Цраг Тцу Лдзо", True),
    ("Ицэ Цраг Лдзо Цо", True),
    ("Ицэ Мцари Зэ Цворэт Лдзо", True),
    ("Мцари Пцак Мдзэкор", True),
    ("Мцари Мцот Мдзэкор", True),
    ("Мцари Дзуна Мдзэкор", True),
    ("Дзорон Лавэ Мдзэкор", True),
    ("Мцари Пцак Тцу Мдзэкор", True),
    ("Дзорон Мцив", True),
    ("Мцари Пцак", False),
    ("Ицэ Цраг", False),  # an NP, not a full clause
    ("Ицэ Црагок Лдзо", False),
    ("Мцари Цвирак", False),
]

def load_lexicon() -> dict[str, str]:
    raw = G17.read_text(encoding="utf-8")
    raw = raw.split("## Отобранные смысловые корни C40 — 40", 1)[1]
    raw = raw.split("## Быстрая автоматизированная сверка", 1)[0]
    out = {}
    for line in raw.splitlines():
        if match := ROW.match(line):
            name, pos = match.groups()
            out[name.casefold()] = pos.strip()
    assert len(out) == 114, "Active G17 form count changed; review G19 fixtures"
    return out

def is_verb(token: str, d: dict[str, str]) -> bool:
    if token in LICENSED_N_TO_V or token == EXPERIENCE:
        return True
    if d.get(token) in {"V", "V+ADV"}:
        return True
    for base, pos in d.items():
        if pos in {"V", "V+ADV"}:
            perfective = "к" if base[-1] in VOWELS else "ок"
            if token == base + perfective:
                return True
    return False

def has_predicate(sentence: str, d: dict[str, str]) -> bool:
    tokens = sentence.casefold().rstrip(".?").split()
    if tokens and tokens[-1] == "цо":
        tokens.pop()
    if not tokens:
        return False
    if tokens[-1] == COPULA:
        i = -2
        if len(tokens) < 3:
            return False
        if tokens[i] == "тцу":
            i -= 1
        # Last item of nominal/REL complement, not a verb or an
        # unrecognized illicit perfective of a nominal root.
        return (
            len(tokens) >= abs(i) + 1
            and d.get(tokens[i]) in {"N", "REL", "NAME"}
        )
    # Verbal head, with independent Tцу immediately preceding it if used.
    return is_verb(tokens[-1], d)

def main() -> None:
    d = load_lexicon()
    for sentence, expected in FIXTURES:
        result = has_predicate(sentence, d)
        assert result == expected, (
            f"Mismatch: {sentence!r}: expected {expected}, got {result}")
    print(f"G19 PASS: {len(FIXTURES)} nominal/experiential/predicate fixtures")
    print("Caveat: this is predicate-head validation, not a complete parser.")

if __name__ == "__main__":
    main()
