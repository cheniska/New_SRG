#!/usr/bin/env python3
"""Experimental Synapt morphology (clean reboot). No old approved names used.
Forms are prototypes, not a translation or canonical lexicon.
"""
from __future__ import annotations
import argparse
import json
import re

ROOTS = {
    "person": ("мцэр", "отдельная личность"),
    "group": ("црав", "совокупность"),
    "think": ("кцэн", "мыслить, различать"),
    "detect": ("дзрим", "обнаруживать"),
    "signal": ("цвэк", "передавать сигнал"),
    "store": ("мдзар", "сохранять"),
    "travel": ("нцор", "перемещаться"),
    "route": ("кцрав", "маршрут"),
    "place": ("цхэм", "место"),
    "light": ("цшар", "излучать свет"),
    "shelter": ("нцэр", "укрывать"),
    "grow": ("мцив", "расти"),
}
DERIVATIONS = {
    "base": ("", "корень"),
    "agent": ("ик", "исполнитель действия"),
    "instrument": ("ац", "орудие или устройство"),
    "process": ("э", "процесс действия"),
    "site": ("ор", "место действия"),
}
# Experimental analytical number; not the plural marker of old prototypes.
NUMBER_PARTICLE = "зэ"
COMPOUND_LINK = "и"
VOWELS = set("аэиоу")

def phonotactic_warnings(word: str) -> list[str]:
    """Simple heuristic, not a biological articulation model."""
    if not re.fullmatch(r"[а-яё]+", word):
        return ["недопустимые символы"]
    warnings = []
    if len(word) > 14:
        warnings.append("длиннее 14 букв")
    if not any(c in VOWELS for c in word):
        warnings.append("нет гласного ядра (слоговой сонорный не отмечен)")
    for match in re.finditer(r"[^аэиоу]+", word):
        if len(match.group()) > 5:
            warnings.append("длинный стык согласных: " + match.group())
    return warnings

def derivation(root_id: str, category: str) -> dict:
    if root_id not in ROOTS:
        raise KeyError("неизвестный корень: " + root_id)
    if category not in DERIVATIONS:
        raise KeyError("неизвестная категория: " + category)
    stem, meaning = ROOTS[root_id]
    suffix, function = DERIVATIONS[category]
    segments = [stem] + ([suffix] if suffix else [])
    form = "".join(segments)
    return {"root_id": root_id, "root": stem, "meaning": meaning,
            "category": category, "suffix": suffix, "function": function,
            "segments": segments, "form": form,
            "warnings": phonotactic_warnings(form)}

def compound(modifier: str, head: str, category: str = "base") -> dict:
    """Modifier precedes head. Long words are warned, not silently clipped."""
    if modifier not in ROOTS or head not in ROOTS:
        raise KeyError("неизвестный корень")
    if category not in DERIVATIONS:
        raise KeyError("неизвестная категория")
    suffix = DERIVATIONS[category][0]
    segments = [ROOTS[modifier][0], COMPOUND_LINK, ROOTS[head][0]]
    if suffix:
        segments.append(suffix)
    form = "".join(segments)
    return {"roots": [modifier, head], "category": category,
            "segments": segments, "form": form,
            "warnings": phonotactic_warnings(form)}

def plural(form: str) -> dict:
    if not re.fullmatch(r"[а-яё]+", form):
        raise ValueError("неверная форма")
    return {"form": form + " " + NUMBER_PARTICLE,
            "segments": [form, NUMBER_PARTICLE],
            "note": "раздельная частица множества (эксперимент)"}

def survey() -> dict:
    rows = [derivation(key, category) for key in ROOTS for category in DERIVATIONS]
    reverse = {}
    for item in rows:
        reverse.setdefault(item["form"], []).append((item["root_id"], item["category"]))
    return {"roots": len(ROOTS), "derived_forms": len(rows),
            "unique_forms": len(reverse),
            "collisions": {k: v for k, v in reverse.items() if len(v) > 1},
            "warning_count": sum(bool(row["warnings"]) for row in rows),
            "samples": [derivation(key, category)
                        for key in ("think", "detect", "signal", "store", "travel")
                        for category in DERIVATIONS]}

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--survey", action="store_true")
    p.add_argument("--root", choices=ROOTS)
    p.add_argument("--category", choices=DERIVATIONS, default="base")
    p.add_argument("--compound", nargs=2, metavar=("MODIFIER", "HEAD"))
    p.add_argument("--plural", action="store_true")
    args = p.parse_args()
    if args.survey or (not args.root and not args.compound):
        output = survey()
    elif args.compound:
        output = compound(*args.compound, category=args.category)
    else:
        output = derivation(args.root, args.category)
    if args.plural and "form" in output:
        output["plural"] = plural(output["form"])
    print(json.dumps(output, ensure_ascii=False, indent=2))

if __name__ == "__main__":
    main()
