import unittest
from word_formation_v1 import derive, compound

class MorphologyTests(unittest.TestCase):
    def test_licensed_derivatives(self):
        for root, role, surface in [
            ("кцор", "agent", "кцорик"), ("пцур", "agent", "пцурик"),
            ("лэриц", "place", "лэрицор"), ("мдзар", "place", "мдзарор"),
            ("цвэк", "instrument", "цвэкац"), ("кцэн", "instrument", "кцэнац"),
            ("кцэн", "result", "кцэнут")]:
            x = derive(root, role)
            self.assertEqual(x.form, surface)
            self.assertEqual(x.status, "candidate")

    def test_semantic_compounds(self):
        for a, b, w in [
            ("нэв", "мцив", "нэвимцив"), ("гар", "вэр", "гаривэр"),
            ("гэц", "мцив", "гэцимцив"), ("кцор", "мцив", "кцоримцив"),
            ("мцур", "мцив", "мцуримцив")]:
            x = compound(a, b)
            self.assertEqual(x.form, w)
            self.assertEqual(x.status, "candidate")

    def test_long_compounds_are_allowed(self):
        for a, b, surface in [
            ("цвэк", "мдзар", "цвэкимдзар"),
            ("нэдар", "ндзэ", "нэдариндзэ"),
            ("сорив", "мдзар", "соривимдзар"),
        ]:
            result = compound(a, b)
            self.assertEqual(result.form, surface)
            self.assertEqual(result.status, "candidate")

    def test_identical_vowels_coalesce_but_morphemes_remain(self):
        x = derive("дзрина", "instrument")
        self.assertEqual(x.form, "дзринац")
        self.assertEqual(x.morphemes, ("дзрина", "ац"))
        self.assertEqual(x.status, "candidate")
        self.assertEqual(x.issues, ())

    def test_connector_preserves_distinct_vowels_but_merges_identical(self):
        self.assertEqual(compound("дзрина", "мцив").form, "дзринаимцив")
        self.assertEqual(compound("кцор", "эцан").form, "кцориэцан")
        self.assertEqual(compound("мциви", "вар").form, "мцививар")
        self.assertEqual(compound("мциви", "вар").morphemes, ("мциви", "и", "вар"))
        self.assertEqual(compound("мцив", "иван").form, "мцививан")

    def test_rejected_phonotactics_still_flagged(self):
        self.assertEqual(derive("чвар", "agent").status, "review")
        self.assertEqual(compound("чвар", "нэв").status, "review")

    def test_invalid_operation(self):
        with self.assertRaises(ValueError):
            derive("кцор", "city")
        with self.assertRaises(ValueError):
            compound("Кцор", "мцив")

if __name__ == "__main__":
    unittest.main()
