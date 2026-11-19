#!/usr/bin/env python3
"""Prototype: Georgian text -> Synapt-styled transcription.
No machine translation. Pass a Georgian phrase already translated/verified.
Python 3.10+, standard library only.
"""
import argparse
import json
import re

# Georgian Mkhedruli to conventional simplified Latin transcription.
GEO = dict(zip(
    "აბგდევზთიკლმნოპჟრსტუფქღყშჩცძწჭხჯჰ",
    ["a","b","g","d","e","v","z","t","i","k","l","m","n","o","p",
     "zh","r","s","t","u","p","k","gh","q","sh","ch","ts","dz",
     "ts","ch","kh","j","h"]))
VOWELS = "aeiou"
# Chosen aesthetic sound shifts, not facts about Georgian.
SINGLE = {"b":"p","p":"b","g":"k","k":"g","d":"t","t":"d",
          "s":"z","z":"s","r":"l","l":"r"}
DIGRAPHS = {"sh":"zh","ch":"ts","gh":"kh","dz":"dzh","kh":"qh","ts":"tsh"}

def syllabify_rough(word):
    """Heuristic only: manual phonetic syllabification is preferable."""
    nuclei = list(re.finditer(r"[aeiou]+", word))
    if len(nuclei) < 2:
        return [word]
    cuts = [m.end() for m in nuclei[:-1]]
    return [word[a:b] for a,b in zip([0]+cuts, cuts+[len(word)])]

def change_sounds(chunk):
    # Digraphs are protected before simultaneous single-letter substitution.
    parts = re.split("(" + "|".join(sorted(DIGRAPHS, key=len, reverse=True)) + ")", chunk)
    return "".join(DIGRAPHS[p] if p in DIGRAPHS else
                   "".join(SINGLE.get(c,c) for c in p) for p in parts)

def transform(word):
    syllables = syllabify_rough(word)
    return "-".join(change_sounds(s) for s in reversed(syllables)), syllables

# Conventional Cyrillic display transcription; digraphs must precede letters.
CYR_TOKENS = {
    "dzh":"дж", "tsh":"цш", "zh":"ж", "sh":"ш", "ch":"ч",
    "ts":"ц", "dz":"дз", "gh":"гъ", "kh":"х", "qh":"къ",
    "ae":"аэ", "w":"в", "q":"къ", "j":"дж",
    "a":"а", "b":"б", "c":"ц", "d":"д", "e":"э", "f":"ф",
    "g":"г", "h":"х", "i":"и", "k":"к", "l":"л", "m":"м",
    "n":"н", "o":"о", "p":"п", "r":"р", "s":"с", "t":"т",
    "u":"у", "v":"в", "x":"кс", "y":"й", "z":"з"
}
CYR_PATTERN = re.compile("|".join(re.escape(x) for x in sorted(CYR_TOKENS, key=len, reverse=True)))

def cyrillic(text):
    return CYR_PATTERN.sub(lambda m: CYR_TOKENS[m.group()], text)

def convert(text):
    rows = []
    for token in re.findall(r"[ა-ჰ]+|[^ა-ჰ]+", text.lower()):
        if not re.fullmatch(r"[ა-ჰ]+", token):
            rows.append({"source":token,"result":token})
            continue
        latin = "".join(GEO[c] for c in token)
        result, syllables = transform(latin)
        rows.append({"source":token,"roman":latin,"syllables":syllables,"result":result,
                     "georgian_ru":cyrillic(latin), "synapt_ru":cyrillic(result)})
    return {"result":"".join(row["result"] for row in rows),
            "georgian_ru":"".join(row.get("georgian_ru",row["result"]) for row in rows),
            "synapt_ru":"".join(row.get("synapt_ru",row["result"]) for row in rows),
            "tokens":rows,
            "note":"Experimental phonological styling, not a real translation or canonical Synapt."}

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("georgian", help="Georgian text (not Russian)")
    args = parser.parse_args()
    print(json.dumps(convert(args.georgian), ensure_ascii=False, indent=2))
