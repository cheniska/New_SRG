import unittest
from synapt_phrase import transform, convert, cyrillic

class SynaptPhraseTests(unittest.TestCase):
    def test_preserve_clusters(self):
        self.assertEqual(transform("mtsveli"), "mtswila")
    def test_short_word_not_collapsed(self):
        self.assertEqual(transform("da"), "de")
    def test_final_vowel_trim(self):
        self.assertEqual(transform("mshvidoba"), "mzhwadub")
    def test_no_overcollapse(self):
        self.assertNotEqual(transform("tsnoba"), transform("dzieba"))
    def test_cyrillic(self):
        self.assertEqual(cyrillic("tshkh"), "цшх")
    def test_phrase(self):
        result = convert("ჩვენ ცოდნას ვეძებთ")
        self.assertEqual(result["version"], "0.3")
        self.assertIn("georgian_ru", result)
        self.assertIn("synapt_ru", result)

if __name__ == "__main__":
    unittest.main()
