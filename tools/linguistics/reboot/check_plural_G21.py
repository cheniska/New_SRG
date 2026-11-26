#!/usr/bin/env python3
"""Fixture-based (NOT canonical) G21 plural morphology and noun relations trial.

Expected corpus: 118 G17 lexemes. The test uses the author-approved neutral
G20 noun-phrase slots, but candidate G21 noun plural -к/-эк and optional
quantifier slot are NOT YET approved by the author.
Run: python3 tools/linguistics/reboot/check_plural_G21.py
"""
from pathlib import Path
import re

DIR = Path(__file__).resolve().parent
REGISTRY = DIR / "active_lexicon_register_G17_2026-11-26.md"
ROW = re.compile(r"^\|\s*\*\*(.+?)\*\*\s*\|\s*([^|]+?)\s*\|")
# These are semantically countable in this trial. Other N may be mass,
# abstract, named entities or sensitive to context.
COUNTABLE = {"цворэт", "цвэк", "мцэр", "нэдар", "цраг"}
VOWELS = set("аэиоуе")

NP_ACCEPT = (
    "Цворэтэк",
    "Цвэкэк",
    "Мцэрэк",
    "Ицэ Цворэтэк",
    "Мцари Зэ Цворэтэк",
    "Дзорон Зэ Цворэтэк",
    "Дзруц Цворэт",
    "Ицэ Дзруц Цворэт",
    "Тцавир Цворэт",
    "Дзэшин Цворэт",
    "Мцэрэк Зэ Цворэт",
    "Мцэр Зэ Цворэтэк",
    "Мцари Зэ Дзруц Цворэт",
    "Цзен Цворэт",
)
NP_REJECT = (
    "Дзруц Цворэтэк",
    "Цзен Цворэтэк",
    "Тцавир Цворэтэк",
    "Ицэ Дзруц Цворэтэк",
    "Цворэтк",
    "Цвэкк",
    "Мцурэк",   # no ordinary plural for mass water
    "Црагк",
    "Мдзарэк",  # V, not N
    "Эцанэк",   # REL, not N
    "Цвэк Цворэт",  # two bare N with no relation marker
    "Мцари Зэ",
    "Цворэтэк Дзруц",
)
SOV_ACCEPT = (
    "Мцари Цворэтэк Мдзар",
    "Дзорон Ицэ Цворэтэк Дзрина",
    "Мцари Дзэшин Цворэт Мдзар",
    "Мцари Дзруц Цворэт Мдзар",
)
POST_ACCEPT = (
    "Ицэ Цворэтэк Тцэна",
    "Мцари Зэ Дзруц Цворэт Пцэро",
)

def load() -> dict[str, str]:
    raw = REGISTRY.read_text(encoding="utf-8")
    raw = raw.split("## Отобранные смысловые корни C40 — 40", 1)[1]
    raw = raw.split("## Быстрая автоматизированная сверка", 1)[0]
    dic = {}
    for line in raw.splitlines():
        if m := ROW.match(line):
            name, pos = m.group(1).lower(), m.group(2).strip()
            assert name not in dic
            dic[name] = pos
    assert len(dic) == 118, f"Expected 118 G17 forms; got {len(dic)}"
    return dic

def typ(form: str, dic: dict[str, str]) -> str | None:
    form = form.lower()
    if form in dic:
        return dic[form]
    if form.endswith("ир") and dic.get(form[:-2]) == "NUM" and form[:-2] != "мдзэш":
        return "ORD"
    if form.endswith("эк") and form[:-2] in COUNTABLE and dic.get(form[:-2]) == "N":
        if form[-3] not in VOWELS:
            return "N.PL"
    if form.endswith("к") and form[:-1] in COUNTABLE and dic.get(form[:-1]) == "N":
        if form[-2] in VOWELS:
            return "N.PL"
    return None

def np(text: str, dic: dict[str, str]) -> bool:
    tokens = text.lower().split()
    i, n = 0, len(tokens)
    if not n:
        return False
    if typ(tokens[i], dic) in {"DET", "WH-DET"}:
        i += 1
    if i+1 < n and tokens[i+1] == "зэ":
        if typ(tokens[i], dic) not in {"N", "N.PL", "PRON", "NAME"}:
            return False
        i += 2
    counted = False
    if i < n and typ(tokens[i], dic) in {"NUM", "ORD", "QUANT"}:
        counted = typ(tokens[i], dic) in {"NUM", "QUANT"}
        i += 1
    if i < n and typ(tokens[i], dic) == "REL":
        i += 1
    if i != n-1:
        return False
    head = typ(tokens[i], dic)
    if counted:
        return head in {"N", "NAME"}  # no duplicated -эк after explicit amount
    return head in {"N", "NAME", "N.PL"}

def sov(text: str, dic: dict[str, str]) -> bool:
    tok = text.lower().split()
    return bool(len(tok) >= 3 and typ(tok[0], dic) == "PRON"
                and typ(tok[-1], dic) in {"V", "V+ADV"}
                and np(" ".join(tok[1:-1]), dic))

def post(text: str, dic: dict[str, str]) -> bool:
    tok = text.lower().split()
    return bool(tok and typ(tok[-1], dic) == "POST"
                and np(" ".join(tok[:-1]), dic))

def main() -> None:
    dic = load()
    fixtures = []
    fixtures += [(x, True, np) for x in NP_ACCEPT]
    fixtures += [(x, False, np) for x in NP_REJECT]
    fixtures += [(x, True, sov) for x in SOV_ACCEPT]
    fixtures += [(x, True, post) for x in POST_ACCEPT]
    for form, expected, checker in fixtures:
        result = checker(form, dic)
        assert result == expected, f"Mismatch {form}: expected {expected}, got {result}"
    assert typ("мдзарок", dic) is None, "Verbal perfective should NOT be noun plural"
    print(f"G21 fixture PASS: {len(fixtures)} phrase tests; 118 current lexemes")
    print("Warning: plural -к/-эк and QUANT slot are G21 proposals, not canon.")
    print("No countable vowel-final noun in this fixture set tests -к directly.")

if __name__ == "__main__":
    main()
