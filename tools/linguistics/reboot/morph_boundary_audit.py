#!/usr/bin/env python3
"""Audit morphology v0.2 and propose reversible boundary repairs.

This utility deliberately does NOT silently change the language or assign
new canonical meanings. It evaluates concatenation of already-defined
morphemes and reports where phonological repair may be needed.
"""
from __future__ import annotations
from collections import defaultdict
import json
import re
from morphology_v2 import (ROOTS, LEXICAL_DERIVATION, NOUN_NUMBER, NOUN_CASE,
                            VERB_TENSE, VERB_ASPECT_PREFIX, VERB_POLARITY_PREFIX,
                            VERB_MOOD, noun, verb)

VOWELS = set("аэиоу")
# Multigraph consonants are one approximate unit; otherwise count letters.
TOKENS = re.compile("дз|дж|[а-яё]")
UNSAFE_STOPS = {"б", "п", "д", "т", "г", "к"}
AFFRICATES = {"дз", "дж", "ц", "ч"}

def tokens(text: str) -> list[str]:
    """Approximate phoneme segmentation; Cyrillic digraphs treated as units."""
    result = TOKENS.findall(text)
    if "".join(result) != text:
        raise ValueError(f"Unsupported transcription: {text!r}")
    return result

def consonant_run(left: str, right: str) -> list[str]:
    ls, rs = tokens(left), tokens(right)
    a, b = [], []
    for x in reversed(ls):
        if x in VOWELS: break
        a.insert(0, x)
    for x in rs:
        if x in VOWELS: break
        b.append(x)
    return a + b

def inspect_boundary(left: str, right: str) -> list[str]:
    if not left or not right:
        return []
    ls, rs = tokens(left), tokens(right)
    alerts = []
    if ls[-1] in VOWELS and rs[0] in VOWELS:
        alerts.append("vowel_hiatus")
    if ls[-1] == rs[0] and ls[-1] not in VOWELS:
        alerts.append("identical_consonants")
    cluster = consonant_run(left, right)
    if len(cluster) > 3:
        alerts.append("four_plus_consonants")
    if any(a in AFFRICATES and b in AFFRICATES for a,b in zip(cluster,cluster[1:])):
        alerts.append("adjacent_affricates")
    if len(cluster) >= 3 and all(x in UNSAFE_STOPS for x in cluster):
        alerts.append("stop_cluster")
    return alerts

def boundary_report(parts: list[str]) -> list[dict]:
    rows = []
    for i in range(len(parts)-1):
        # A boundary is evaluated on entire prefix, because its last sounds
        # can come from more than one morpheme.
        left = "".join(parts[:i+1])
        right = parts[i+1]
        problems = inspect_boundary(left, right)
        if problems:
            rows.append({"index":i, "left":parts[i], "right":right,
                         "problems":problems, "context":"".join(parts)})
    return rows

def audit():
    noun_forms = [noun(k, derived=d, number=n, case=c)
                  for k in ROOTS for d in LEXICAL_DERIVATION
                  for n in NOUN_NUMBER for c in NOUN_CASE]
    verb_forms = [verb(k, tense=t, aspect=a, polarity=p, mood=m)
                  for k in ROOTS for t in VERB_TENSE
                  for a in VERB_ASPECT_PREFIX
                  for p in VERB_POLARITY_PREFIX for m in VERB_MOOD]
    def summary(forms):
        by_surface = defaultdict(list)
        boundary_types = defaultdict(int)
        examples = []
        for item in forms:
            by_surface[item["form"]].append(item["tags"])
            found = boundary_report(item["morphemes"])
            for issue in found:
                for kind in issue["problems"]:
                    boundary_types[kind] += 1
                if len(examples) < 12:
                    examples.append(issue)
        return {"forms":len(forms),"distinct_surfaces":len(by_surface),
                "homographs":{k:v for k,v in by_surface.items() if len(v)>1},
                "boundary_issue_counts":dict(boundary_types),
                "boundary_examples":examples}
    return {"noun":summary(noun_forms),"verb":summary(verb_forms),
            "note":"Counts are for an experimental paradigm, not claims about real speech."}

if __name__=="__main__":
    print(json.dumps(audit(),ensure_ascii=False,indent=2))
