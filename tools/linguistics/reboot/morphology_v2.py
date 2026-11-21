#!/usr/bin/env python3
"""Synapt conlang reboot: experimental agglutinative morphology v0.2.

Root compounding creates new lexemes; suffixes inflect nouns and verbs;
prefixes alter polarity or aspect.  This is a deliberately small, TESTABLE
grammar sketch, not a translation system or established canon.
"""
from __future__ import annotations
import argparse
import json
import re
from dataclasses import dataclass

# Lexical roots are exploratory, shared with the earlier reboot experiment.
from morphology import ROOTS

# No gender agreement assumed. "Plural" marks count, not social group.
NOUN_NUMBER = {"sg": "", "pl": "к"}
NOUN_CASE = {
    "nom": "",     # citation form / subject (working model)
    "acc": "эс",   # patient / direct object
    "gen": "эн",   # possessor
    "dat": "ир",   # recipient, goal
    "loc": "эц",   # location
    "ins": "ум",   # instrument, means
}
VERB_TENSE = {"prs": "а", "pst": "ит", "fut": "ор"}
VERB_ASPECT_PREFIX = {"simple": "", "ongoing": "мэ"}
VERB_POLARITY_PREFIX = {"pos": "", "neg": "шэ"}
LEXICAL_DERIVATION = {
    "bare": "", "agent": "ик", "instrument": "ац", "event": "э", "site": "ор"
}
COMPOUND_LINK = "и"
VOWELS = "аэиоу"


@dataclass(frozen=True)
class WordForm:
    spelling: str
    morphemes: tuple[str, ...]
    tags: dict[str, str]
    gloss: str

    def as_dict(self) -> dict:
        return {
            "form": self.spelling, "morphemes": list(self.morphemes),
            "tags": self.tags, "gloss": self.gloss,
            "warnings": phonology_warnings(self.spelling)
        }


def phonology_warnings(form: str) -> list[str]:
    """Warnings, not a proof of physiological pronounceability."""
    errors = []
    if not re.fullmatch(r"[а-яё]+", form):
        errors.append("non-Cyrillic character")
    if len(form) > 18:
        errors.append("long word")
    # Cyrillic digraphs like дз represent units; don't overinterpret letters.
    runs = re.findall(r"[^аэиоу]+", form)
    if any(len(x) > 6 for x in runs):
        errors.append("large consonant junction")
    return errors


def lexeme(*roots: str, derived: str = "bare") -> tuple[str, tuple[str, ...]]:
    if not 1 <= len(roots) <= 2:
        raise ValueError("lexemes must have 1 or 2 roots")
    if derived not in LEXICAL_DERIVATION:
        raise ValueError("unknown derivation")
    if any(root not in ROOTS for root in roots):
        raise ValueError("unknown lexical root")
    pieces = []
    for i, root in enumerate(roots):
        if i:
            pieces.append(COMPOUND_LINK)
        pieces.append(ROOTS[root][0])
    suffix = LEXICAL_DERIVATION[derived]
    if suffix:
        pieces.append(suffix)
    return "".join(pieces), tuple(pieces)


def noun(*roots: str, derived: str = "bare",
         number: str = "sg", case: str = "nom") -> dict:
    if number not in NOUN_NUMBER or case not in NOUN_CASE:
        raise ValueError("unknown noun form")
    base, pieces = lexeme(*roots, derived=derived)
    parts = pieces + tuple(x for x in (NOUN_NUMBER[number], NOUN_CASE[case]) if x)
    return WordForm("".join(parts), parts,
                    {"pos": "noun", "roots": "+".join(roots),
                     "derived": derived, "number": number, "case": case},
                    "ROOT[-DERIV]-NUMBER-CASE").as_dict()


def verb(root: str, tense: str = "prs", aspect: str = "simple",
         polarity: str = "pos") -> dict:
    if root not in ROOTS:
        raise ValueError("unknown root")
    if tense not in VERB_TENSE or aspect not in VERB_ASPECT_PREFIX or polarity not in VERB_POLARITY_PREFIX:
        raise ValueError("unknown verb features")
    # Fixed ordering avoids ambiguous choices:
    # NEGATIVE > ASPECT > ROOT > TENSE
    pieces = tuple(x for x in (
        VERB_POLARITY_PREFIX[polarity], VERB_ASPECT_PREFIX[aspect],
        ROOTS[root][0], VERB_TENSE[tense]
    ) if x)
    return WordForm("".join(pieces), pieces,
                    {"pos": "verb", "root": root, "tense": tense,
                     "aspect": aspect, "polarity": polarity},
                    "NEG-ASPECT-ROOT-TENSE").as_dict()


def sentence(subject: str, action: str, obj: str | None = None,
             tense: str = "prs", polarity: str = "pos") -> dict:
    """Experimental SOV with nominative subject and accusative object."""
    words = [noun(subject)]
    if obj is not None:
        words.append(noun(obj, case="acc"))
    words.append(verb(action, tense=tense, polarity=polarity))
    return {
        "text": " ".join(w["form"] for w in words),
        "words": words,
        "status": "illustrative word order, not approved grammar"
    }


def survey() -> dict:
    derived = [noun(k, derived=d, number=n, case=c)
               for k in ROOTS for d in LEXICAL_DERIVATION
               for n in NOUN_NUMBER for c in NOUN_CASE]
    seen = {}
    for word in derived:
        seen.setdefault(word["form"], []).append(word["tags"])
    return {
        "nominal_forms": len(derived),
        "unique": len(seen),
        "collisions": {k: v for k, v in seen.items() if len(v)>1},
        "warnings": [x for x in derived if x["warnings"]][:20],
        "examples": [
            noun("detect", derived="instrument", number="pl", case="loc"),
            noun("signal", "detect", derived="instrument", case="acc"),
            verb("detect", tense="pst", polarity="neg"),
            sentence("person", "detect", "signal", tense="pst")
        ]
    }


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    commands = p.add_subparsers(dest="command", required=True)
    pn = commands.add_parser("noun")
    pn.add_argument("roots", nargs="+")
    pn.add_argument("--derived", choices=LEXICAL_DERIVATION, default="bare")
    pn.add_argument("--number", choices=NOUN_NUMBER, default="sg")
    pn.add_argument("--case", choices=NOUN_CASE, default="nom")
    pv = commands.add_parser("verb")
    pv.add_argument("root")
    pv.add_argument("--tense", choices=VERB_TENSE, default="prs")
    pv.add_argument("--aspect", choices=VERB_ASPECT_PREFIX, default="simple")
    pv.add_argument("--polarity", choices=VERB_POLARITY_PREFIX, default="pos")
    ps = commands.add_parser("sentence")
    ps.add_argument("subject")
    ps.add_argument("action")
    ps.add_argument("--object")
    ps.add_argument("--tense", choices=VERB_TENSE, default="prs")
    ps.add_argument("--polarity", choices=VERB_POLARITY_PREFIX, default="pos")
    commands.add_parser("survey")
    args = p.parse_args()
    if args.command == "noun":
        result = noun(*args.roots, derived=args.derived,
                      number=args.number, case=args.case)
    elif args.command == "verb":
        result = verb(args.root, tense=args.tense, aspect=args.aspect,
                      polarity=args.polarity)
    elif args.command == "sentence":
        result = sentence(args.subject, args.action, args.object,
                          args.tense, args.polarity)
    else:
        result = survey()
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
