import unittest
from boundary_phonology import surface


class BoundaryPhonologyTests(unittest.TestCase):
    def test_double_consonant_is_long_not_erased(self):
        result = surface(["цвэк", "ик", "к", "эс"])
        self.assertEqual(result["spelling"], "цвэкиккэс")
        self.assertEqual(result["pronunciation"], "цвэкикːэс")
        self.assertEqual(result["changes"][0]["kind"], "geminate")

    def test_front_vowel_glide(self):
        result = surface(["дзрим", "э", "эс"])
        self.assertEqual(result["spelling"], "дзримээс")
        self.assertEqual(result["pronunciation"], "дзримэйэс")
        self.assertEqual(result["changes"][0]["kind"], "glide")

    def test_rounded_vowel_glide(self):
        result = surface(["мцэ", "ум"])
        self.assertEqual(result["pronunciation"], "мцэўум")

    def test_a_vowel_hiatus_not_erased(self):
        result = surface(["дзрима", "ац"])
        self.assertEqual(result["pronunciation"], "дзримаац")
        self.assertEqual(result["changes"][0]["kind"], "hiatus")

    def test_plain_boundary(self):
        result = surface(["цвэк", "и", "дзрим", "ац"])
        self.assertEqual(result["changes"], [])
        self.assertEqual(result["pronunciation"], result["spelling"])

    def test_ambiguous_phonetic_reconstruction_preserves_morphemes(self):
        result = surface(["цвэк", "ик", "к"])
        self.assertEqual(result["morphemes"], ["цвэк", "ик", "к"])

    def test_bad_input_rejected(self):
        with self.assertRaises(ValueError):
            surface([])
        with self.assertRaises(ValueError):
            surface(["мцэр", ""])
        with self.assertRaises(ValueError):
            surface(["invalid"])


if __name__ == "__main__":
    unittest.main()
