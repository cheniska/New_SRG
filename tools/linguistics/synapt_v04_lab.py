#!/usr/bin/env python3
"""Synapt v0.4 sound-law laboratory, standard library only.

Works on verified Georgian-source Latin transliterations; does NOT translate.
Two conservative historical sound-law variants, preserving consonant clusters.
Every output includes source and ordered transformation trace.
"""
from __future__ import annotations
import argparse
import json
import re

# Georgian transliteration is provided explicitly to avoid falsely equating
# Georgian orthography, Cyrillic transcription and IPA.
VOWELS = "aeiou"
# Only context-conditioned changes; not global Caesar-style substitution.
RULES = (
    ("intervocalic_v", r"(?<=[aeiou])v(?=[aeiou])", "w"),
    ("intervocalic_g", r"(?<=[aeiou])g(?=[aeiou])", "gh"),
    ("initial_sh", r"^sh", "zh"),
    ("final_a_after_consonant", r"(?<=[bcdfghjklmnpqrstvwxyz])a$", "e"),
)
# Russian display, not a claim of exact pronunciation.
DISPLAY = {"gh":"гъ","zh":"ж","sh":"ш","ch":"ч","ts":"ц","dz":"дз",
           "kh":"х","w":"в","q":"къ","j":"дж"}
DISPLAY.update({"a":"а","e":"э","i":"и","o":"о","u":"у","v":"в","t":"т",
                "s":"с","z":"з","p":"п","b":"б","g":"г","d":"д","r":"р",
                "l":"л","m":"м","n":"н","k":"к","f":"ф","h":"х","y":"й"})
TOKEN_RE = re.compile("|".join(map(re.escape,sorted(DISPLAY,key=len,reverse=True))))

def cyr(text: str) -> str:
    return TOKEN_RE.sub(lambda m: DISPLAY[m.group()],text)

def validate(word: str) -> list[str]:
    errors=[]
    if not re.fullmatch(r"[a-z]+",word):
        errors.append("Latin a-z transcription required")
        return errors
    if not any(v in word for v in VOWELS):
        errors.append("No vowel nucleus")
    if len(word)>22:
        errors.append("Unusually long word")
    # Count letters, not phonemes: heuristic only.
    for cluster in re.findall(r"[^aeiou]+",word):
        if len(cluster)>5:
            errors.append("Long consonant cluster: "+cluster)
    return errors

def transform(word: str, variant: str="A") -> dict:
    word=word.lower()
    problems=validate(word)
    if problems and "Latin a-z transcription required" in problems:
        raise ValueError(problems[0])
    current=word
    trace=[]
    rules=RULES[:3] if variant=="A" else RULES[:4]
    if variant not in ("A","B"):
        raise ValueError("variant must be A or B")
    for name,pattern,replacement in rules:
        nxt=re.sub(pattern,replacement,current)
        if nxt!=current:
            trace.append({"rule":name,"before":current,"after":nxt})
            current=nxt
    if variant=="B":
        # Limited reduction: only final -e after 2+ consonants, only in
        # words with >= 3 vowel nuclei, preserving >= 2 vowel nuclei.
        if len(current)>=8 and sum(c in VOWELS for c in current)>=3:
            nxt=re.sub(r"([^aeiou]{2,})e$",r"\1",current)
            if nxt!=current:
                trace.append({"rule":"final_e_after_cluster","before":current,"after":nxt})
                current=nxt
    return {"source":word,"variant":variant,"latin":current,"ru":cyr(current),
            "trace":trace,"warnings":validate(current)}

def batch(words: list[str]) -> dict:
    rows=[{"source":w,"A":transform(w,"A"),"B":transform(w,"B")} for w in words]
    for variant in ("A","B"):
        reverse={}
        for row in rows:
            reverse.setdefault(row[variant]["latin"],[]).append(row["source"])
        for row in rows:
            row[variant]["collision"]=len(reverse[row[variant]["latin"]])>1
    return {"version":"0.4-lab","rows":rows}

if __name__=="__main__":
    p=argparse.ArgumentParser()
    p.add_argument("words",nargs="+",help="verified Georgian Latin transcriptions")
    args=p.parse_args()
    print(json.dumps(batch(args.words),ensure_ascii=False,indent=2))
