#!/usr/bin/env python3
"""Synapt reboot: boundary phonology v0.1.

Three distinct representations:
  underlying: immutable concatenation of morphemes;
  orthography: identical vowels coalesce; other morphemes stay visible;
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

    Identical vowels coalesce in spelling and pronunciation: а+а -> а.
    Geminate consonants stay doubled in spelling; phonetic aid marks length.
    Unlike identical vowels, distinct adjacent vowels remain distinct in
    spelling. A phonetic glide is represented for э/и or о/у
    onsets in this older illustrative model; its reality is not settled.
    """
    if not parts:
        raise ValueError("at least one morpheme required")
    units = [tokenize(p) for p in parts]
    underlying = "".join(parts)
    spelling: list[str] = list(units[0])
    pronunciation: list[str] = list(units[0])
    changes: list[dict] = []
    for index, unit in enumerate(units[1:], 1):
        previous = units[index - 1]
        last, first = previous[-1], unit[0]
        if last == first and last in VOWELS:
            # Productive vowel coalescence at a morpheme boundary:
            # дзрина+ац => дзринац, not дзринаац.
            # Keep underlying morphemes and do not infer an ancient sound law.
            spelling.extend(unit[1:])
            pronunciation.extend(unit[1:])
            changes.append({"boundary": index, "kind": "identical_vowel_coalescence",
                            "between": last + "|" + first})
        elif last == first and last not in VOWELS:
            spelling.extend(unit)
            # Preserve both consonants in written form, mark length in phonetic aid.
            pronunciation[-1] = pronunciation[-1] + LONG_MARK
            pronunciation.extend(unit[1:])
            changes.append({"boundary": index, "kind": "geminate",
                            "between": last + "|" + first})
        elif last in VOWELS and first in VOWELS:
            spelling.extend(unit)
            glide = "й" if first in FRONT else "ў" if first in ROUNDED else None
            if glide:
                pronunciation.append(glide)
                changes.append({"boundary": index, "kind": "glide",
                                "sound": glide, "between": last + "|" + first})
            else:
                changes.append({"boundary": index, "kind": "hiatus",
                                "between": last + "|" + first})
            pronunciation.extend(unit)
        else:
            spelling.extend(unit)
            pronunciation.extend(unit)
    return {
        "morphemes": list(parts),
        "underlying": underlying,
        "spelling": "".join(spelling),
        "pronunciation": "".join(pronunciation),
        "changes": changes,
    }


def split_examples() -> list[dict]:
    return [
        surface(["цвэк", "ик", "к", "эс"]),
        surface(["дзрина", "ац"]),
        surface(["цвэк", "и", "дзрим", "ац"]),
        surface(["цхэм", "эц"]),
        surface(["мэ", "дзрим", "ит"]),
    ]


if __name__ == "__main__":
    import json
    print(json.dumps(split_examples(), ensure_ascii=False, indent=2))
