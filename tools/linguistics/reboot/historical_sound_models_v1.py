#!/usr/bin/env python3
"""Compare three constructed preforms of experimental Synapt roots.

These are CONSTRUCTED counterfactual models, not recovered historical data.
The change rules are restricted to the initial consonant cluster, are tested
across all 30 input forms, and NEVER alter approved modern spellings.
"""
from __future__ import annotations

from collections import Counter
from dataclasses import dataclass

# First 30 roots, in exact order of roots_trial_C_game_lexicon.md.
MODERN_ROOTS = (
    "црэван", "кцур", "мцав", "мцур", "гар", "мцив", "зэви", "тэвар",
    "нцор", "дзэван", "ндзэ", "цвэк", "мдзар", "кцэн", "эцан",
    "нцэрат", "кцэтор", "пцэви", "тцора", "киван", "дзрина",
    "мцэр", "нэдар", "сорив", "тирак", "ндзавэр", "лэриц",
    "гэвин", "цворэт", "вэтик",
)

# (proto initial, modern initial). Only hypothesized word-initial changes.
NASAL = (("мт", "мц"), ("нт", "нц"), ("мд", "мдз"), ("нд", "ндз"))
STOP = (("кт", "кц"), ("тр", "цр"), ("др", "дзр"),
        ("тв", "цв"), ("пт", "пц"), ("тт", "тц"))

MODELS = {"nasal": NASAL, "stop": STOP, "combined": NASAL + STOP}


@dataclass(frozen=True)
class Analysis:
    modern: str
    ancient: str
    changed: bool


def _apply(word: str, rules: tuple[tuple[str, str], ...],
           *, reverse: bool = False) -> str:
    for proto, modern in sorted(rules, key=lambda x: -len(x[1] if reverse else x[0])):
        old, new = (modern, proto) if reverse else (proto, modern)
        if word.startswith(old):
            return new + word[len(old):]
    return word


def reconstruct(modern: str, model: str) -> str:
    if model not in MODELS:
        raise ValueError(f"unknown model {model!r}")
    return _apply(modern, MODELS[model], reverse=True)


def evolve(ancient: str, model: str) -> str:
    if model not in MODELS:
        raise ValueError(f"unknown model {model!r}")
    return _apply(ancient, MODELS[model])


def audit(roots: tuple[str, ...] = MODERN_ROOTS) -> dict:
    if len(roots) != len(set(roots)):
        raise ValueError("input contains duplicate modern roots")
    results = {}
    for model in MODELS:
        rows = [Analysis(w, reconstruct(w, model),
                         reconstruct(w, model) != w) for w in roots]
        # Every reconstructed ancient form must yield the input modern form.
        roundtrip_errors = [r for r in rows if evolve(r.ancient, model) != r.modern]
        collision_counts = Counter(r.ancient for r in rows)
        collisions = {w: n for w, n in collision_counts.items() if n > 1}
        results[model] = {
            "rules": len(MODELS[model]),
            "changed": sum(r.changed for r in rows),
            "unchanged": sum(not r.changed for r in rows),
            "roundtrip_errors": len(roundtrip_errors),
            "ancient_collisions": collisions,
            "rows": [{"modern": r.modern, "ancient": r.ancient,
                      "changed": r.changed} for r in rows],
        }
    return results


if __name__ == "__main__":
    import json
    print(json.dumps(audit(), ensure_ascii=False, indent=2))
