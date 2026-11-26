#!/usr/bin/env python3
"""Regression fixtures for the proposed G20 neutral noun-phrase order.

This is a small structural test, not a natural-language parser. No external
packages; run from any working directory:
    python3 tools/linguistics/reboot/check_noun_phrases_G20.py

G20 NP proposal: [DET] [OWNER Зэ] [NUM|ORD] [REL] NOUN
Approval: this order is an experimental recommendation; preexisting words,
the G4/G5 forms, ordinal -ир, SOV and G9 postpositions are the sources.
"""
from __future__ import annotations
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REGISTRY = ROOT / "active_lexicon_register_G17_2026-11-26.md"
ROW = re.compile(r"^\|\s*\*\*(.+?)\*\*\s*\|\s*([^|]+?)\s*\|")

NP_ACCEPT = (
    "Цворэт",
    "Ицэ Цворэт",
    "Ицэв Цвэк",
    "Мцари Зэ Цворэт",
    "Мцэни Зэ Цворэт",
    "Дзрэн Зэ Цворэт",
    "Дзруц Цворэт",
    "Нцэлин Цвэк",
    "Цзенир Цворэт",
    "Ицэ Дзруц Цворэт",
    "Мцари Зэ Цзенир Цворэт",
    "Ицэ Мцари Зэ Цзенир Цворэт",
    "Эцан Мцэр",
    "Ицэ Эцан Мцэр",
    "Цвэрин Цворэт",
)

NP_REJECT = (
    "Дзруц Ицэ Цворэт",
    "Ицэ Цворэт Дзруц",
    "Цзенир Дзруц Цворэт",
    "Зэ Мцари Цворэт",
    "Мцари Зэ",
    "Цвирак Цворэт",
    "Мцари Зэ Цворэт Цзенир",
    "Ицэ Эцан",
)

SOV_ACCEPT = (
    "Мцари Дзруц Цворэт Мдзар",
    "Дзорон Мцари Зэ Цзенир Цворэт Мдзар",
    "Мцари Ицэ Дзруц Цворэт Мдзар",
    "Дзорон Дзрэн Зэ Цворэт Мдзар",
    "Дзорон Ицэ Эцан Мцэр Дзрина",
    "Мцари Цвэрин Цворэт Мдзар",
)

POST_ACCEPT = (
    "Ицэ Дзруц Цворэт Тцэна",
    "Мцари Зэ Цзенир Цворэт Пцэро",
)

def load_pos() -> dict[str, str]:
    text = REGISTRY.read_text(encoding="utf-8")
    text = text.split("## Отобранные смысловые корни C40 — 40", 1)[1]
    text = text.split("## Быстрая автоматизированная сверка", 1)[0]
    out = {}
    for line in text.splitlines():
        if match := ROW.match(line):
            word, pos = match.groups()
            key = word.casefold()
            assert key not in out, f"Duplicate word: {word}"
            out[key] = pos.strip()
    assert len(out) == 114, f"Expected 114 active words, found {len(out)}"
    return out

def typ(word: str, pos: dict[str, str]) -> str | None:
    w = word.casefold()
    if pos.get(w):
        return pos[w]
    if w.endswith("ир"):
        base = w[:-2]
        if pos.get(base) == "NUM" and base != "мдзэш":
            return "ORD"
    return None

def parse_np(tokens: list[str], pos: dict[str, str]) -> bool:
    i, n = 0, len(tokens)
    if not n:
        return False
    if typ(tokens[i], pos) in {"DET", "WH-DET"}:
        i += 1
    # For the first version, possessors are SIMPLE: one PRON, N or NAME
    # followed by the approved free relation marker Зэ.
    if i + 1 < n and tokens[i+1].casefold() == "зэ":
        if typ(tokens[i], pos) not in {"PRON", "N", "NAME"}:
            return False
        i += 2
    if i < n and typ(tokens[i], pos) in {"NUM", "ORD"}:
        i += 1
    if i < n and typ(tokens[i], pos) == "REL":
        i += 1
    if i >= n or typ(tokens[i], pos) not in {"N", "NAME"}:
        return False
    i += 1
    return i == n

def check_sentence(text: str, pos: dict[str, str]) -> bool:
    tokens = text.casefold().split()
    if len(tokens) < 3 or typ(tokens[0], pos) != "PRON":
        return False
    if typ(tokens[-1], pos) not in {"V", "V+ADV"}:
        return False
    return parse_np(tokens[1:-1], pos)

def check_postphrase(text: str, pos: dict[str, str]) -> bool:
    tokens = text.casefold().split()
    return bool(tokens) and typ(tokens[-1], pos) == "POST" and parse_np(tokens[:-1], pos)

def main() -> None:
    pos = load_pos()
    cases = (
        [(text, True, parse_np) for text in NP_ACCEPT]
        + [(text, False, parse_np) for text in NP_REJECT]
    )
    for text, expected, fn in cases:
        actual = fn(text.lower().split(), pos)
        assert actual == expected, f"NP: {text} expected {expected}, got {actual}"
    for text in SOV_ACCEPT:
        assert check_sentence(text, pos), f"SOV: {text}"
    for text in POST_ACCEPT:
        assert check_postphrase(text, pos), f"POST: {text}"
    count = len(cases) + len(SOV_ACCEPT) + len(POST_ACCEPT)
    print(f"G20: {count} structural fixtures pass; 114-entry G17 lexicon loaded")
    print("Warning: syntax/semantics of G20 ordering remain to be approved by the author.")

if __name__ == "__main__":
    main()
