#!/usr/bin/env python3
"""Synapt phonology v0.2: Georgian -> conservative alien adaptation.

Input is Georgian, NOT Russian; semantic translation is a separate step.
Russian-script output is a display transcription, not IPA.
All transformations are deterministic. Exact reversal needs the source record.
"""
import argparse
import json
import re

GEO = dict(zip(
    "აბგდევზთიკლმნოპჟრსტუფქღყშჩცძწჭხჯჰ",
    ["a","b","g","d","e","v","z","t","i","k","l","m","n","o","p",
     "zh","r","s","t","u","p","k","gh","q","sh","ch","ts","dz",
     "ts","ch","kh","j","h"]))
# Orthographic Georgian distinctions are retained only approximately in this
# prototype. Do not use as authoritative Georgian phonetic transcription.
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

def cyrillic(text):
    return CYR_RE.sub(lambda m: CYR[m.group()], text)

def syllables(word):
    """Conservative nuclei-based segments, retaining onset clusters intact.

    A cluster between two vowels belongs to the following segment.
    This is an algorithmic approximation, NOT Georgian linguistic syllabification.
    """
    nuclei = list(re.finditer("[aeiou]+", word))
    if len(nuclei) < 2:
        return [word]
    cuts = [n.end() for n in nuclei[:-1]]
    return [word[a:b] for a,b in zip([0]+cuts, cuts+[len(word)])]

# Sparse, cluster-friendly replacements; not global permutations.
# Replacements happen once, simultaneously, on original grapheme tokens.
SHIFTS = {"sh":"zh", "ch":"ts", "gh":"kh", "v":"w"}
TOKEN_RE = re.compile("sh|ch|gh|[a-z]")

def soften(word):
    return TOKEN_RE.sub(lambda m: SHIFTS.get(m.group(), m.group()), word)

def compact(word):
    """Only reduce obvious vowel hiatus; never delete a whole syllable."""
    word = re.sub(r"([aeiou])\\1+", r"\\1", word)
    # Three consecutive vowels become two; avoid collapsing all vowels.
    word = re.sub(r"([aeiou])([aeiou])([aeiou])", r"\\1\\3", word)
    return word

def transform(word):
    original = syllables(word)
    # Preserve first segment and all its onset consonants. For 4+ segments,
    # swap only the final two; 1-3 segments keep original order.
    segments = original[:]
    if len(segments) >= 4:
        segments[-2], segments[-1] = segments[-1], segments[-2]
    joined = "".join(segments)
    result = compact(soften(joined))
    # Do not allow transformations to collapse a meaningful source word.
    if len(result) < 3 and len(word) >= 3:
        result = soften(word)
    return result, original

def convert(text):
    rows = []
    for token in re.findall(r"[ა-ჰ]+|[^ა-ჰ]+", text.lower()):
        if not re.fullmatch(r"[ა-ჰ]+", token):
            rows.append({"source":token, "georgian_ru":token, "synapt_ru":token})
            continue
        latin = "".join(GEO[c] for c in token)
        output, chunks = transform(latin)
        rows.append({"source":token, "roman":latin, "segments":chunks,
                     "georgian_ru":cyrillic(latin),
                     "synapt_ru":cyrillic(output), "synapt_latin":output})
    return {
        "georgian_ru":"".join(x["georgian_ru"] for x in rows),
        "synapt_ru":"".join(x["synapt_ru"] for x in rows),
        "tokens":rows,
        "version":"0.2",
        "note":"Experimental phonological adaptation, not a real Synapt translation."
    }

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("georgian", help="Georgian text (not Russian)")
    args = parser.parse_args()
    print(json.dumps(convert(args.georgian), ensure_ascii=False, indent=2))
