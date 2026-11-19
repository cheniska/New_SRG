#!/usr/bin/env python3
"""Experimental two-parent name formation for Tnacshk.
Both parents contribute equally. No hereditary surname or gender marking.
"""
import argparse
import json
import re

VOWELS = set("аеёиоуыэюя")
CONSONANTS = set("бвгджзйклмнпрстфхцчшщ")
LINK = "э"

def root(name: str, count: int = 3) -> str:
    chars = "".join(c for c in name.lower() if c in CONSONANTS)
    if len(chars) < count:
        raise ValueError(f"Insufficient consonants in {name!r}")
    return chars[:count]

def parental_marker(parent_a: str, parent_b: str, link: str = LINK) -> dict:
    roots = sorted((root(parent_a), root(parent_b)))
    # Order-independent: parent_a/parent_b may be swapped without changing marker.
    return {"roots": roots, "compressed": "".join(roots),
            "readable": link.join(roots), "link": link}

def full_name(person: str, parent_a: str, parent_b: str) -> dict:
    marker = parental_marker(parent_a, parent_b)
    return {"name": person, "parents": [parent_a, parent_b],
            "parent_marker": marker,
            "full": person + " " + marker["readable"].capitalize(),
            "note": "Experimental naming proposal; identical parent roots may collide."}

if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("name")
    p.add_argument("parent_a")
    p.add_argument("parent_b")
    args = p.parse_args()
    print(json.dumps(full_name(args.name, args.parent_a, args.parent_b), ensure_ascii=False, indent=2))
