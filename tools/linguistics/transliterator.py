#!/usr/bin/env python3
"""Deterministic alien-language prototype. Python 3.10+, standard library only."""
from __future__ import annotations
import argparse
import json
import re

VOWELS = "aeiou"
# Profile rules operate on Latin phoneme-like transcription, NOT Georgian spelling.
# These are INVENTED regular shifts, not claims about Georgian phonetics.
PROFILES = {
    "synapt": {
        "version": "0.1",
        "shifts": [("sh", "zh"), ("ch", "ts"), ("q", "kh"), ("v", "w")],
        "glottal_before": ("a", "e"),
        "consonant_maps": {
            "none": {},
            "rotation": {"r": "d", "d": "r", "b": "g", "g": "t", "t": "b"},  # legacy demonstration
            "synapt_cluster": {"s": "z", "z": "s", "t": "d", "d": "t",
                               "k": "g", "g": "k", "p": "b", "b": "p"},
        },
    }
}

def rough_syllables(word: str) -> list[str]:
    """Heuristic only: prefer manually separated syllables for Georgian clusters."""
    word = word.lower().strip()
    if not re.fullmatch(r"[a-z]+", word):
        raise ValueError("Use Latin transcription a-z; manual syllables separated with '-'.")
    groups = list(re.finditer(r"[aeiou]+", word))
    if len(groups) <= 1:
        return [word]
    boundaries = []
    for left, right in zip(groups, groups[1:]):
        # Keep all intervening consonants with next syllable.
        boundaries.append(left.end())
    return [word[a:b] for a, b in zip([0] + boundaries, boundaries + [len(word)])]

def encode(word: str, profile: str = "synapt", syllables: str | None = None,
           reverse: bool = True, apostrophe: bool = True,
           consonant_map: str = "synapt_cluster") -> dict:
    if profile not in PROFILES:
        raise ValueError("Unknown profile")
    if consonant_map not in PROFILES[profile]["consonant_maps"]:
        raise ValueError("Unknown consonant map")
    source = word.lower().strip()
    if not re.fullmatch(r"[a-z]+", source):
        raise ValueError("Source must be Latin transcription a-z")
    if syllables:
        chunks = syllables.lower().split("-")
        if not chunks or any(not re.fullmatch(r"[a-z]+", c) for c in chunks):
            raise ValueError("Manual syllables must be hyphen-separated Latin chunks")
        if "".join(chunks) != source:
            raise ValueError("Syllables must concatenate to source exactly")
    else:
        chunks = rough_syllables(source)
    ordered = chunks[::-1] if reverse else chunks[:]
    # Use temporary sentinels to prevent cascading shift replacements.
    shifts = PROFILES[profile]["shifts"]
    def shift(chunk: str) -> str:
        for i, (old, _) in enumerate(shifts):
            chunk = chunk.replace(old, chr(0xE000 + i))
        for i, (_, new) in enumerate(shifts):
            chunk = chunk.replace(chr(0xE000 + i), new)
        return chunk
    transformed = [shift(c) for c in ordered]
    mapping = PROFILES[profile]["consonant_maps"][consonant_map]
    # str.translate applies the permutation simultaneously, never cascading.
    table = str.maketrans(mapping)
    transformed = [c.translate(table) for c in transformed]
    surface = "-".join(transformed)
    # Apostrophe marks an explicit glottal onset at a syllable boundary,
    # and does not appear merely for visual effect.
    if apostrophe:
        surface = re.sub(r"-([ae])", r"'\1", surface)
    return {
        "profile": profile, "version": PROFILES[profile]["version"],
        "source": source, "source_syllables": chunks,
        "reverse_syllables": reverse, "apostrophe": apostrophe,
        "consonant_map": consonant_map,
        "result": surface, "meaning": None,
    }

def decode(record: dict) -> str:
    """Exact reconstruction using the saved source and deterministic verification."""
    expected = encode(record["source"], record["profile"],
                      "-".join(record["source_syllables"]),
                      record["reverse_syllables"], record["apostrophe"],
                      record.get("consonant_map", "none"))
    if expected["result"] != record["result"]:
        raise ValueError("Record differs from reproducible output")
    return record["source"]

def main() -> None:
    parser = argparse.ArgumentParser(description="Alien phonology prototype")
    sub = parser.add_subparsers(dest="cmd", required=True)
    enc = sub.add_parser("encode")
    enc.add_argument("word", help="Latin transcription of source word")
    enc.add_argument("--syllables", help="Manual segmentation, e.g. ga-mar-jo-ba")
    enc.add_argument("--profile", default="synapt", choices=PROFILES)
    enc.add_argument("--no-reverse", action="store_true")
    enc.add_argument("--no-apostrophe", action="store_true")
    enc.add_argument("--consonant-map", default="synapt_cluster", choices=PROFILES["synapt"]["consonant_maps"])
    dec = sub.add_parser("decode")
    dec.add_argument("record", help="JSON object from encode output")
    args = parser.parse_args()
    if args.cmd == "encode":
        print(json.dumps(encode(args.word, args.profile, args.syllables,
                                not args.no_reverse, not args.no_apostrophe,
                                args.consonant_map),
                         ensure_ascii=False, indent=2))
    else:
        print(decode(json.loads(args.record)))

if __name__ == "__main__":
    main()
