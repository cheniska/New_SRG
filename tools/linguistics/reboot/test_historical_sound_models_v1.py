"""Regression tests for speculative Synapt preform scenarios, NOT historical validation."""
import unittest

from historical_sound_models_v1 import (
    MODERN_ROOTS, MODELS, reconstruct, evolve, audit
)

BIOLOGY_HOLDOUT = ("нэв", "вэр", "гэц", "кцор", "дэв",
                   "вар", "пцур", "тар", "вир", "тэмар")


class HistoricalSoundModelsTests(unittest.TestCase):
    def test_all_thirty_round_trip(self):
        self.assertEqual(len(MODERN_ROOTS), 30)
        results = audit()
        for model, details in results.items():
            with self.subTest(model=model):
                self.assertEqual(details["roundtrip_errors"], 0)
                self.assertEqual(details["ancient_collisions"], {})

    def test_change_counts(self):
        results = audit()
        self.assertEqual(
            {model: result["changed"] for model, result in results.items()},
            {"nasal": 9, "stop": 9, "combined": 18},
        )
        self.assertEqual(
            {model: result["rules"] for model, result in results.items()},
            {"nasal": 4, "stop": 6, "combined": 10},
        )

    def test_each_rule_applies_to_all_eligible_roots(self):
        for model in MODELS:
            for modern in MODERN_ROOTS:
                with self.subTest(model=model, modern=modern):
                    ancient = reconstruct(modern, model)
                    self.assertEqual(evolve(ancient, model), modern)

    def test_holdout_has_no_extra_rules(self):
        self.assertEqual(len(BIOLOGY_HOLDOUT), 10)
        self.assertEqual(
            sum(reconstruct(w, "nasal") != w for w in BIOLOGY_HOLDOUT), 0,
        )
        self.assertEqual(
            [(w, reconstruct(w, "stop")) for w in BIOLOGY_HOLDOUT
             if reconstruct(w, "stop") != w],
            [("кцор", "ктор"), ("пцур", "птур")],
        )

    def test_models_are_not_attested_etymologies(self):
        # Example outputs are explicitly *hypotheses*, not canon.
        self.assertEqual(reconstruct("црэван", "stop"), "трэван")
        self.assertEqual(reconstruct("мцав", "nasal"), "мтав")
        self.assertEqual(reconstruct("ндзэ", "nasal"), "ндэ")

    def test_unknown_model_rejected(self):
        with self.assertRaises(ValueError):
            reconstruct("мцав", "invented")


if __name__ == "__main__":
    unittest.main()
