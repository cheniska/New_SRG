#!/usr/bin/env python3
"""Regression checks for the experimental Synapt v1 naming profile.

Run from this directory:
    python -m unittest test_phonotactics_v1.py

Tests are deterministic but the threshold ranges intentionally allow
algorithmic adjustments; these are *style* checks, not linguistic laws.
"""
import unittest
import phonotactics as p

COMMON = ("мц", "нц", "мдз", "ндз", "цр", "дзр", "цв", "кц", "пц", "тц")
RARE = ("мцр", "кцв", "нцр", "цш", "цх", "чр")
STARTS = tuple(sorted(COMMON + RARE, key=len, reverse=True))


class SynaptV1Tests(unittest.TestCase):
    def test_v1_shapes_and_forbidden_patterns(self):
        words = p.generate(240, seed=22011226, style="synapt-v1",
                           min_syllables=1, max_syllables=2)
        self.assertEqual(len(words), 240)
        for item in words:
            word = item["form"]
            self.assertGreaterEqual(len(word), 3)
            self.assertLessEqual(len(word), 9)
            self.assertFalse(word.startswith("чв"), word)
            self.assertFalse(word.endswith("м"), word)
            syllables = [
                p.Syllable(tuple(row["onset"]), row["nucleus"],
                           tuple(row["coda"]))
                for row in item["syllables"]
            ]
            self.assertEqual(p.check_word(syllables), [], word)
            self.assertEqual(p.render(syllables), word)

    def test_v1_strong_but_not_uniform(self):
        words = p.generate(400, seed=22011226, style="synapt-v1",
                           min_syllables=2, max_syllables=2)
        strong = sum(any(item["form"].startswith(prefix) for prefix in STARTS)
                     for item in words)
        proportion = strong / len(words)
        self.assertGreater(proportion, 0.54)
        self.assertLess(proportion, 0.86)
        hush = sum("ш" in item["form"] or "ч" in item["form"] for item in words)
        self.assertLess(hush / len(words), 0.12)

    def test_legacy_seed_determinism(self):
        for style in ("balanced", "chitter", "synapt-v1"):
            a = p.generate(24, seed=1234, style=style)
            b = p.generate(24, seed=1234, style=style)
            self.assertEqual(a, b)

    def test_approved_names_allowed_in_existing_inventory(self):
        home = [p.Syllable(("ц", "р"), "э"),
                p.Syllable(("м",), "и", ("н",))]
        star = [p.Syllable(("м", "ц"), "а", ("ш",))]
        self.assertEqual(p.render(home), "црэмин")
        self.assertEqual(p.render(star), "мцаш")
        self.assertEqual(p.check_word(home), [])
        self.assertEqual(p.check_word(star), [])


if __name__ == "__main__":
    unittest.main()
