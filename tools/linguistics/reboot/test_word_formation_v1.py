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

    def test_long_compounds_are_flagged(self):
        for a, b in [("цвэк", "мдзар"), ("нэдар", "ндзэ"), ("сорив", "мдзар")]:
            self.assertEqual(compound(a, b).status, "review")

    def test_vowel_hiatus_not_silently_repaired(self):
        x = derive("дзрина", "instrument")
        self.assertEqual(x.form, "дзринаац")
        self.assertIn("unresolved vowel hiatus at a morpheme boundary", x.issues)
        self.assertEqual(x.status, "review")

    def test_connector_only_between_consonant_roots(self):
        self.assertEqual(compound("дзрина", "мцив").status, "review")
        self.assertEqual(compound("кцор", "эцан").status, "review")

    def test_invalid_operation(self):
        with self.assertRaises(ValueError):
            derive("кцор", "city")
        with self.assertRaises(ValueError):
            compound("Кцор", "мцив")

if __name__ == "__main__":
    unittest.main()
