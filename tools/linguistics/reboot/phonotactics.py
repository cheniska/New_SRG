#!/usr/bin/env python3
"""Experimental Synapt language phonotactics, independent of all legacy names.

Generates *forms only*, not translations or approved vocabulary.
Standard library only; deterministic with --seed. Internal syllabic r/n
are explicit tokens R/N; Russian display uses р/н.
"""
from __future__ import annotations

import argparse
import json
import random
from dataclasses import dataclass

# Token strings are PHONEME-LIKE units, not individual letters. Dz is one unit.
ONSETS = {
    "simple": [("м",), ("н",), ("к",), ("т",), ("ш",), ("ц",), ("дз",), ("ч",), ("з",), ("в",)],
    "chitter": [
        ("м", "ц"), ("н", "ц"), ("м", "дз"), ("н", "дз"), ("ц", "р"),
        ("дз", "р"), ("ц", "в"), ("к", "ц"), ("п", "ц"),
        ("т", "ц"), ("ц", "ш"), ("ц", "х"), ("ч", "р"),
        ("м", "ц", "р"), ("к", "ц", "в"), ("н", "ц", "р")
    ],
    "quiet": [(), ("р",), ("л",), ("г",), ("д",), ("с",), ("п",), ("дж",)],
    # Author-approved rare initial лдз- (Лдзо): accepted, not productive in generation.
    "rare_approved": [("л", "дз")],
}
NUCLEI = [("а",), ("э",), ("и",), ("о",), ("у",), ("е",), ("R",), ("N",)]
CODAS = [(), ("к",), ("т",), ("н",), ("р",), ("в",), ("ш",), ("г",),
         ("ц",), ("й",), ("р", "к"), ("н", "ц"), ("ц", "к")]
# 26.11.2026: Cyrillic е is a permitted RARE vowel spelling (Цзен,
# Пцэвен, Джев, Ндзерак). It may not yet have a separately approved
# phonemic status relative to э; do not normalize selected names.
# The weighted synapt-v1 generator uses rare е (weight 1 of total 19);
# legacy chitter/balanced sampling stays unchanged for reproducibility.
# Approved Лдзо uses a rare л+дз onset, validated but not generated freely.
# Rare final г (Цраг), й (Нэй) and initial дж (Джев) are accepted
# for selected roots; they remain absent from weighted V1 generation.
# Rare final г is accepted for the author-selected root Цраг; it remains absent
# from weighted V1_CODAS to avoid making it a common generative ending.
# Forbidden token-sequences across syllable junctions: avoid accidental
# adjacency of two affricates or unmanageably long consonant strings.
AFFRICATES = {"ц", "дз", "ч", "дж"}
VOWELS = {"а", "э", "и", "о", "у", "е"}
SYLLABIC = {"R", "N"}
CONSONANTS = set(x for group in ONSETS.values() for onset in group for x in onset)
CONSONANTS |= set(x for coda in CODAS for x in coda)
CONSONANTS -= VOWELS

@dataclass(frozen=True)
class Syllable:
    onset: tuple[str, ...]
    nucleus: str
    coda: tuple[str, ...] = ()

    def tokens(self) -> tuple[str, ...]:
        return self.onset + (self.nucleus,) + self.coda

def render(syllables: list[Syllable]) -> str:
    return "".join("р" if token == "R" else "н" if token == "N"
                   else token for s in syllables for token in s.tokens())

def check_syllable(s: Syllable) -> list[str]:
    errors = []
    if not any(s.onset in group for group in ONSETS.values()):
        errors.append("unknown onset")
    if (s.nucleus,) not in NUCLEI:
        errors.append("unknown nucleus")
    if s.coda not in CODAS:
        errors.append("unknown coda")
    if s.nucleus in SYLLABIC and s.coda and s.coda[0] in {"р", "н"}:
        errors.append("repeated sonorant after syllabic nucleus")
    return errors

def check_word(syllables: list[Syllable]) -> list[str]:
    errors = []
    if not 1 <= len(syllables) <= 3:
        errors.append("word must have 1-3 syllables")
    for s in syllables:
        errors.extend(check_syllable(s))
    for a, b in zip(syllables, syllables[1:]):
        boundary = a.coda + b.onset
        if len(boundary) > 3:
            errors.append("overlong medial consonant junction")
        if any(x in AFFRICATES and y in AFFRICATES
               for x, y in zip(boundary, boundary[1:])):
            errors.append("two adjacent affricates")
        if len(boundary) >= 3 and all(x in {"к", "т", "п", "г", "д", "б"}
                                       for x in boundary):
            errors.append("three stop consonants in a row")
    return errors

# Candidate production profile v1: weighted style, not a new phoneme inventory.
# Previous chitter/balanced modes remain unchanged for reproducibility.
V1_COMMON_CHITTER = [
    ("м", "ц"), ("н", "ц"), ("м", "дз"), ("н", "дз"),
    ("ц", "р"), ("дз", "р"), ("ц", "в"), ("к", "ц"),
    ("п", "ц"), ("т", "ц")
]
V1_RARE_CHITTER = [
    ("м", "ц", "р"), ("к", "ц", "в"), ("н", "ц", "р"),
    ("ц", "ш"), ("ц", "х"), ("ч", "р")
]
V1_SIMPLE = [("м",), ("н",), ("к",), ("т",), ("ц",),
             ("дз",), ("з",), ("в",), ("р",), ("л",),
             ("г",), ("д",), ("с",), ("п",), ()]
V1_VOWELS = ["а", "э", "и", "о", "у", "е"]
V1_CODAS = [(), (), (), (), ("н",), ("р",), ("в",),
            ("к",), ("т",), ("ц",), ("ш",)]
V1_VOWEL_WEIGHTS = [5, 5, 3, 3, 2, 1]

def choose_v1_syllable(
    rng: random.Random, *, first: bool, last: bool,
    allow_syllabic: bool,
) -> Syllable:
    """Weighted game-naming style, not an attested language frequency."""
    u = rng.random()
    if first:
        family = "strong" if u < .68 else ("rare" if u < .72 else "simple")
    else:
        family = "strong" if u < .31 else ("rare" if u < .33 else "simple")
    options = (V1_COMMON_CHITTER if family == "strong"
               else V1_RARE_CHITTER if family == "rare" else V1_SIMPLE)
    onset = rng.choice(options)
    nucleus = rng.choices(V1_VOWELS, weights=V1_VOWEL_WEIGHTS, k=1)[0]
    if allow_syllabic and rng.random() < .035 and onset and onset[-1] not in {"р", "н"}:
        nucleus = rng.choice(["R", "N"])
    if not last and rng.random() < .78:
        coda = ()
    else:
        coda = rng.choice(V1_CODAS)
    return Syllable(onset, nucleus, coda)

def choose_syllable(rng: random.Random, style: str, allow_syllabic: bool) -> Syllable:
    families = ["chitter", "chitter", "chitter", "simple", "quiet"] if style == "chitter" else [
        "chitter", "simple", "simple", "quiet", "quiet"]
    onset = rng.choice(ONSETS[rng.choice(families)])
    # Syllabic R/N are possible but rare, and not available with every onset.
    nucleus = rng.choice(["а", "э", "и", "о", "у"])
    if allow_syllabic and rng.random() < 0.10 and onset and onset[-1] not in {"р", "н"}:
        nucleus = rng.choice(["R", "N"])
    coda = rng.choice(CODAS)
    return Syllable(onset, nucleus, coda)

def generate(count: int, seed: int = 42, style: str = "chitter",
             min_syllables: int = 1, max_syllables: int = 2,
             syllabic: bool = True) -> list[dict]:
    if count < 1 or count > 10000:
        raise ValueError("count must be 1..10000")
    if style not in {"chitter", "balanced", "synapt-v1"}:
        raise ValueError("style must be chitter, balanced or synapt-v1")
    if not (1 <= min_syllables <= max_syllables <= 3):
        raise ValueError("syllables must be in range 1..3")
    rng = random.Random(seed)
    results, used = [], set()
    attempts = 0
    while len(results) < count and attempts < count * 500:
        attempts += 1
        n = rng.randint(min_syllables, max_syllables)
        syllables = ([choose_v1_syllable(rng, first=(i == 0), last=(i == n-1), allow_syllabic=syllabic) for i in range(n)] if style == "synapt-v1" else [choose_syllable(rng, style, syllabic) for _ in range(n)])
        errors = check_word(syllables)
        word = render(syllables)
        # Reject too-short, overlong and identical display forms.
        if errors or len(word) < 3 or len(word) > 9 or word in used:
            continue
        used.add(word)
        results.append({
            "form": word,
            "syllables": [
                {"onset": list(s.onset), "nucleus": s.nucleus,
                 "coda": list(s.coda)} for s in syllables],
            "syllabic_sonorant": any(s.nucleus in SYLLABIC for s in syllables)
        })
    if len(results) < count:
        raise RuntimeError("insufficient unique valid words for constraints")
    return results

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--count", type=int, default=30)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--style", choices=["chitter", "balanced", "synapt-v1"], default="synapt-v1")
    parser.add_argument("--min-syllables", type=int, default=1)
    parser.add_argument("--max-syllables", type=int, default=2)
    parser.add_argument("--no-syllabic", action="store_true")
    args = parser.parse_args()
    words = generate(args.count, args.seed, args.style,
                     args.min_syllables, args.max_syllables,
                     not args.no_syllabic)
    print(json.dumps({"status":"experimental", "seed":args.seed,
                      "style":args.style, "words":words},
                     ensure_ascii=False, indent=2))

if __name__ == "__main__":
    main()
