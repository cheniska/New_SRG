import unittest
from boundary_phonology import surface


class BoundaryPhonologyTests(unittest.TestCase):
    def test_double_consonant_is_long_not_erased(self):
        result = surface(["цвэк", "ик", "к", "эс"])
        self.assertEqual(result["spelling"], "цвэкиккэс")
        self.assertEqual(result["pronunciation"], "цвэкикːэс")
        self.assertEqual(result["changes"][0]["kind"], "geminate")

    def test_identical_front_vowels_merge(self):
        result = surface(["дзрим", "э", "эс"])
        self.assertEqual(result["underlying"], "дзримээс")
        self.assertEqual(result["spelling"], "дзримэс")
        self.assertEqual(result["pronunciation"], "дзримэс")
        self.assertEqual(result["changes"][0]["kind"], "identical_vowel_coalescence")

    def test_different_front_vowels_keep_spelling_without_forced_glide(self):
        result = surface(["мцэ", "ик"])
        self.assertEqual(result["spelling"], "мцэик")
        self.assertEqual(result["pronunciation"], "мцэик")
        self.assertEqual(result["changes"][0]["kind"], "distinct_vowel_sequence")

    def test_different_rounded_vowels_no_invented_glide(self):
        result = surface(["мцэ", "ум"])
        self.assertEqual(result["pronunciation"], "мцэум")
        self.assertEqual(result["changes"][0]["kind"], "distinct_vowel_sequence")

    def test_identical_a_vowels_coalesce(self):
        result = surface(["дзрина", "ац"])
        self.assertEqual(result["morphemes"], ["дзрина", "ац"])
        self.assertEqual(result["underlying"], "дзринаац")
        self.assertEqual(result["spelling"], "дзринац")
        self.assertEqual(result["pronunciation"], "дзринац")
        self.assertEqual(result["changes"][0]["kind"], "identical_vowel_coalescence")

    def test_different_vowels_can_remain_adjacent(self):
        result = surface(["дзрина", "ик"])
        self.assertEqual(result["spelling"], "дзринаик")
        self.assertEqual(result["changes"][0]["kind"], "distinct_vowel_sequence")

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
