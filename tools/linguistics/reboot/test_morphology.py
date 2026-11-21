import unittest
from morphology import ROOTS, DERIVATIONS, derivation, compound, plural, survey

class TestMorphology(unittest.TestCase):
    def test_all_stems_and_categories(self):
        for stem in ROOTS:
            for category in DERIVATIONS:
                item = derivation(stem, category)
                self.assertEqual("".join(item["segments"]), item["form"])
                self.assertEqual(item["warnings"], [])

    def test_morphological_family(self):
        self.assertEqual(derivation("detect", "base")["form"], "дзрим")
        self.assertEqual(derivation("detect", "agent")["form"], "дзримик")
        self.assertEqual(derivation("detect", "instrument")["form"], "дзримац")
        self.assertEqual(derivation("detect", "site")["form"], "дзримор")

    def test_compound_order_matters(self):
        self.assertNotEqual(compound("signal", "detect")["form"],
                            compound("detect", "signal")["form"])

    def test_plural_is_separate(self):
        self.assertEqual(plural("мцэр")["form"], "мцэр зэ")

    def test_no_derivation_collisions(self):
        self.assertEqual(survey()["collisions"], {})

    def test_invalid(self):
        with self.assertRaises(KeyError):
            derivation("invalid", "agent")
        with self.assertRaises(KeyError):
            derivation("think", "invalid")

if __name__ == "__main__":
    unittest.main()
