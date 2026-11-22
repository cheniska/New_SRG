"""Experimental Synapt word formation; preserves boundaries and surface spelling.

Long words and vowel hiatus are permitted following author feedback.
Phonetic realization of hiatus is not specified by this generator.
"""
from dataclasses import dataclass
import re

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
    return _evaluate(root + suffix, (root, suffix), (len(root),))

def compound(modifier: str, head: str) -> WordFormationResult:
    _check_part(modifier)
    _check_part(head)
    joined = modifier + "и" + head
    result = _evaluate(joined, (modifier, "и", head), (len(modifier), len(modifier) + 1))
    issues = list(result.issues)
    return WordFormationResult(joined, result.morphemes,
                               "review" if issues else "candidate", tuple(issues))
