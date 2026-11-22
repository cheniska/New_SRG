"""Experimental Synapt word formation; preserves boundaries and surface spelling.

Long words and different adjacent vowels are permitted. Identical vowels
coalesce only at morpheme boundaries; underlying morphemes stay intact.
"""
from dataclasses import dataclass
import re
from boundary_phonology import surface

VOWELS = frozenset("аэиоу")
SUFFIXES = {"agent": "ик", "instrument": "ац", "place": "ор", "result": "ут"}

@dataclass(frozen=True)
class WordFormationResult:
    form: str
    morphemes: tuple[str, ...]
    status: str
    issues: tuple[str, ...]

def _check_part(part: str) -> None:
    if not part or not re.fullmatch("[а-я]+", part):
        raise ValueError("each component must be a nonempty lowercase Cyrillic string")

def _evaluate(form: str, morphemes: tuple[str, ...], boundaries: tuple[int, ...]) -> WordFormationResult:
    issues = []
    if form.startswith("чв") or form.endswith("м"):
        issues.append("rejected phonotactic sequence")
    return WordFormationResult(form, morphemes, "review" if issues else "candidate", tuple(issues))

def derive(root: str, function: str) -> WordFormationResult:
    _check_part(root)
    if function not in SUFFIXES:
        raise ValueError("unsupported derivational suffix function")
    suffix = SUFFIXES[function]
    parts = (root, suffix)
    return _evaluate(surface(parts)["spelling"], parts, (len(root),))

def compound(modifier: str, head: str) -> WordFormationResult:
    _check_part(modifier)
    _check_part(head)
    parts = (modifier, "и", head)
    return _evaluate(surface(parts)["spelling"], parts,
                     (len(modifier), len(modifier) + 1))
