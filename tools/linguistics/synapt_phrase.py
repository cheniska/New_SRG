#!/usr/bin/env python3
"""Experimental Synapt phonology v0.3.

Accepts verified Georgian text (NOT Russian). Outputs Cyrillic transcriptions.
Retains Georgian consonant clusters while changing vowels and some consonants.
Not a translator, not an IPA transcription. No names are automatically canon.
"""
from __future__ import annotations
import argparse
import json
import re

GEO = dict(zip(
    "აბგდევზთიკლმნოპჟრსტუფქღყშჩცძწჭხჯჰ",
    ["a","b","g","d","e","v","z","t","i","k","l","m","n","o","p",
     "zh","r","s","t","u","p","k","gh","q","sh","ch","ts","dz",
     "ts","ch","kh","j","h"]))
VOWELS = "aeiou"
CYR = {
    "dzh":"дж", "tsh":"цш", "zh":"ж", "sh":"ш", "ch":"ч",
    "ts":"ц", "dz":"дз", "gh":"гъ", "kh":"х", "qh":"къ",
    "w":"в", "q":"къ", "j":"дж",
    "a":"а", "b":"б", "c":"ц", "d":"д", "e":"э", "f":"ф",
    "g":"г", "h":"х", "i":"и", "k":"к", "l":"л", "m":"м",
    "n":"н", "o":"о", "p":"п", "r":"р", "s":"с", "t":"т",
    "u":"у", "v":"в", "x":"кс", "y":"й", "z":"з"
}
CYR_RE = re.compile("|".join(re.escape(x) for x in sorted(CYR, key=len, reverse=True)))

def cyrillic(text: str) -> str:
    return CYR_RE.sub(lambda m: CYR[m.group()], text)

# These are authorial sound laws, NOT descriptions of Georgian.
# Most consonants and all original consonant clusters survive intact.
DIGRAPH_SHIFT = {"sh":"zh", "ch":"ts", "gh":"kh"}
SINGLE_SHIFT = {"v":"w", "j":"zh"}
VOWEL_SHIFT = {"a":"e", "e":"i", "i":"a", "o":"u", "u":"o"}
TOKENS = re.compile("sh|ch|gh|[a-z]")

def shift(word: str) -> str:
    def repl(m):
        p = m.group()
        return DIGRAPH_SHIFT.get(p, SINGLE_SHIFT.get(p, VOWEL_SHIFT.get(p, p)))
    return TOKENS.sub(repl, word)

def compact(word: str) -> str:
    """Trim only a weak FINAL open vowel of long words, if safe.

    Never remove internal syllabic vowels or split consonant clusters.
    Keep at least one vowel and >= 4 characters in resulting word.
    """
    if len(word) >= 8 and word[-1:] in VOWELS and sum(c in VOWELS for c in word) >= 3:
        return word[:-1]
    return word

def transform(word: str) -> str:
    if not re.fullmatch("[a-z]+", word):
        raise ValueError("Expected lowercase Latin transcription")
    return compact(shift(word))

def convert(text: str) -> dict:
    rows = []
    for token in re.findall(r"[ა-ჰ]+|[^ა-ჰ]+", text.lower()):
        if not re.fullmatch(r"[ა-ჰ]+", token):
            rows.append({"source":token,"georgian_ru":token,"synapt_ru":token})
            continue
        latin = "".join(GEO[c] for c in token)
        result = transform(latin)
        rows.append({"source":token,"roman":latin,"synapt_latin":result,
                     "georgian_ru":cyrillic(latin),"synapt_ru":cyrillic(result)})
    return {"version":"0.3",
            "georgian_ru":"".join(x["georgian_ru"] for x in rows),
            "synapt_ru":"".join(x["synapt_ru"] for x in rows),
            "tokens":rows,
            "note":"Experimental transformation; not a real Georgian translation or canonical Synapt."}

if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("georgian", help="Georgian phrase")
    args = p.parse_args()
    print(json.dumps(convert(args.georgian), ensure_ascii=False, indent=2))
