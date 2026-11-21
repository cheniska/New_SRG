#!/usr/bin/env python3
"""Ten experimental sample utterances for Synapt morphology.
Do not mistake these for translations in a finished constructed language.
"""
from __future__ import annotations
import json
from morphology_v2 import noun, verb

SAMPLES = [
    ("Личность обнаруживает сигнал.", ["person"], ["signal"], "detect", "prs", "pos", "simple"),
    ("Личность обнаружила сигнал.", ["person"], ["signal"], "detect", "pst", "pos", "simple"),
    ("Личность обнаружит сигнал.", ["person"], ["signal"], "detect", "fut", "pos", "simple"),
    ("Личность не обнаруживает сигнал.", ["person"], ["signal"], "detect", "prs", "neg", "simple"),
    ("Личность продолжает обнаруживать сигнал.", ["person"], ["signal"], "detect", "prs", "pos", "ongoing"),
    ("Группа хранит сигнал.", ["group"], ["signal"], "store", "prs", "pos", "simple"),
    ("Личность передала сигнал.", ["person"], ["signal"], "signal", "pst", "pos", "simple"),
    ("Личность сохранит прибор обнаружения.", ["person"], ["detect"], "store", "fut", "pos", "simple"),
    ("Группа не будет хранить сигнал.", ["group"], ["signal"], "store", "fut", "neg", "simple"),
    ("Личность обнаруживает устройство обнаружения сигналов.", ["person"], ["signal", "detect"], "detect", "prs", "pos", "simple"),
]

def trials() -> list[dict]:
    rows = []
    for i, (meaning, subject, obj, action, tense, polarity, aspect) in enumerate(SAMPLES, 1):
        subj = noun(*subject)
        target = noun(*obj, derived="instrument" if len(obj) > 1 or i == 8 else "bare", case="acc")
        predicate = verb(action, tense=tense, polarity=polarity, aspect=aspect)
        # Experimental order is subject-object-verb, not agreed syntax.
        words = [subj, target, predicate]
        rows.append({
            "number": i, "intended_meaning": meaning,
            "text": " ".join(item["form"] for item in words),
            "pronunciation": " ".join(item["pronunciation"] for item in words),
            "words": words, "syntax": "SOV (experimental)",
        })
    return rows

if __name__ == "__main__":
    print(json.dumps(trials(), ensure_ascii=False, indent=2))
