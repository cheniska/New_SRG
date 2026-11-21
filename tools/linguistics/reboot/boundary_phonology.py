#!/usr/bin/env python3
"""Synapt reboot: boundary phonology v0.1.

Three distinct representations:
  underlying: immutable concatenation of morphemes;
  orthography: same letters, no decorative separators;
  pronunciation: annotated long consonants and optional glides.

This module does not assert how an alien vocal organ physically works.
"""
from __future__ import annotations

import re

VOWELS = set("аэиоу")
FRONT = {"э", "и"}
ROUNDED = {"о", "у"}
TOKEN_RE = re.compile(r"дз|дж|[а-яё]")
LONG_MARK = "ː"


def tokenize(part: str) -> list[str]:
    if not part:
        raise ValueError("empty morpheme")
    result = TOKEN_RE.findall(part)
    if "".join(result) != part:
        raise ValueError(f"unsupported transcription: {part!r}")
    return result


def surface(parts: list[str] | tuple[str, ...]) -> dict:
    """Apply phonetic transitions strictly at morpheme boundaries.

    Geminates are retained in spelling; pronounced as one long consonant.
    Before a vowel-initial morpheme, a transition /j/ or /w/ may emerge:
    e/i => j (displayed й); o/u => w (displayed ў).
    a+a or a+e after a vowel are not blindly rewritten: only the following
    vowel class determines the epenthetic glide (e => й; a => hiatus).
    """
    if not parts:
        raise ValueError("at least one morpheme required")
    units = [tokenize(p) for p in parts]
    orthographic = "".join(parts)
    pronunciation: list[str] = list(units[0])
    changes: list[dict] = []
    for index, unit in enumerate(units[1:], 1):
        previous = units[index - 1]
        last, first = previous[-1], unit[0]
        if last == first and last not in VOWELS:
            # The preceding consonant is already in the output. Represent
            # the *pair* as one long consonant, do not repeat it.
            pronunciation[-1] = pronunciation[-1] + LONG_MARK
            pronunciation.extend(unit[1:])
            changes.append({"boundary":index, "kind":"geminate",
                            "between":last + "|" + first})
        elif last in VOWELS and first in VOWELS:
            glide = "й" if first in FRONT else "ў" if first in ROUNDED else None
            if glide:
                pronunciation.append(glide)
                changes.append({"boundary":index, "kind":"glide",
                                "sound":glide, "between":last+"|"+first})
            else:
                changes.append({"boundary":index, "kind":"hiatus",
                                "between":last+"|"+first})
            pronunciation.extend(unit)
        else:
            pronunciation.extend(unit)
    return {
        "morphemes":list(parts),
        "underlying":orthographic,
        "spelling":orthographic,
        "pronunciation":"".join(pronunciation),
        "changes":changes
    }


def split_examples() -> list[dict]:
    return [
        surface(["цвэк", "ик", "к", "эс"]),
        surface(["дзрим", "э", "эс"]),
        surface(["цвэк", "и", "дзрим", "ац"]),
        surface(["цхэм", "эц"]),
        surface(["мэ", "дзрим", "ит"]),
    ]


if __name__ == "__main__":
    import json
    print(json.dumps(split_examples(), ensure_ascii=False, indent=2))
