#!/usr/bin/env python3
"""Non-destructive audit of the selected Ndzeraк lexicon (G17).

Run from any working directory:
    python3 tools/linguistics/reboot/audit_active_lexicon_G17.py
    python3 tools/linguistics/reboot/audit_active_lexicon_G17.py --json

The audit identifies Cyrillic edit-distance-1 pairs and rare orthographic
variants (including author-approved е and лдз-); it does NOT decide whether words are acoustically confusable.
"""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent

SOURCES = (
    ("C40", "roots_trial_C_game_lexicon.md"),
    ("G3", "everyday_lexicon_trial_34_2026-11-25.md"),
    ("G16", "numerals_ordinals_decimal_marker_trial_G16_2026-11-26.md"),
)
# Separate current grammatical, quantifier and measure forms, excluding archived variants.
GRAMMAR = (
    "Зэ", "Дзрэн", "Цвэри", "Цвэра", "Цвэрин", "Цвэрот",
    "Цвэтан", "Цвэнац", "Цвэшиц", "Тцэна", "Пцэро",
    "Нцави", "Нцэшар", "Пцэран", "Тэ", "Лдзо",
    "Тцавир", "Дзэшин", "Пзотц", "Цваран",
)
NAMES = (
    "Ндзерак", "Дзерак", "Црэмин", "Мцаш", "Мцквар",
    "Дзгрэт", "Цхар", "Дзрэв", "Кцэр", "Цхаридзрэв",
)

ENTRY_RE = re.compile(r"^\|\s*(\d+)\s*\|\s*\*\*(.+?)\*\*\s*\|")

def rows(path: Path, source: str) -> list[str]:
    text = path.read_text(encoding="utf-8")
    if source == "G16":
        # Don't accidentally count bold phrases in the later example table.
        text = text.split("## 1. Отбор числительных", 1)[-1]
        text = text.split("## 2. Порядковые числа", 1)[0]
    return [m.group(2).strip() for line in text.splitlines()
            if (m := ENTRY_RE.match(line))]

def levenshtein(a: str, b: str) -> int:
    a, b = a.casefold(), b.casefold()
    prev = list(range(len(a) + 1))
    for j, bj in enumerate(b, start=1):
        curr = [j]
        for i, ai in enumerate(a, start=1):
            curr.append(min(curr[-1] + 1, prev[i] + 1,
                            prev[i - 1] + (ai != bj)))
        prev = curr
    return prev[-1]

def audit() -> dict:
    groups = {label: rows(ROOT / filename, label)
              for label, filename in SOURCES}
    groups["grammar"] = list(GRAMMAR)
    groups["names"] = list(NAMES)
    forms: dict[str, str] = {}
    duplicates: list[tuple[str, str]] = []
    for label, items in groups.items():
        for item in items:
            key = item.casefold()
            if key in forms:
                duplicates.append((forms[key], label + ":" + item))
            else:
                forms[key] = item
    words = list(forms.values())
    near = [[x, y] for i, x in enumerate(words)
            for y in words[i + 1:] if levenshtein(x, y) == 1]
    unusual = {
        "rare_permitted_letter_e":
            [word for word in words if "е" in word.casefold()],
        "initial_cz": [w for w in words if w.lower().startswith("цз")],
        "initial_pz": [w for w in words if w.lower().startswith("пз")],
        "final_tc": [w for w in words if w.lower().endswith("тц")],
        "rare_approved_copula_onset_ldz": [w for w in words if w.casefold().startswith("лдз")],
        "notable_proper_name_clusters":
            [w for w in words if w in ("Мцквар", "Дзгрэт")],
    }
    return {
        "counts": {k: len(v) for k, v in groups.items()},
        "unique_forms": len(words),
        "duplicate_forms": duplicates,
        "distance_one_pairs": near,
        "distance_one_total": len(near),
        "orthography_checks": unusual,
        "caution": ("Cyrillic edit distance is not a phonetic measurement. "
                    "Candidate status and semantic differences need human review."),
    }

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args()
    data = audit()
    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return
    print("Sources:", data["counts"])
    print("Unique forms:", data["unique_forms"])
    print("Exact source duplicates:", data["duplicate_forms"])
    print("One-edit pairs:", data["distance_one_total"])
    for left, right in data["distance_one_pairs"]:
        print(f"  {left} ~ {right}")
    for key, items in data["orthography_checks"].items():
        print(f"{key}: {', '.join(items) if items else '(none)'}")
    print(data["caution"])

if __name__ == "__main__":
    main()
