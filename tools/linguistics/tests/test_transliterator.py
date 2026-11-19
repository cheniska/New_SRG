import unittest
from transliterator import encode, decode

class TestSynapt(unittest.TestCase):
    def test_round_trip(self):
        for word, syllables in [
            ("gamarjoba", "ga-mar-jo-ba"),
            ("sakartvelo", "sa-kar-tve-lo"),
            ("mtsvane", "mtsva-ne"),
        ]:
            rec = encode(word, syllables=syllables)
            self.assertEqual(decode(rec), word)
    def test_apostrophe_is_phonetic_boundary(self):
        rec = encode("khae", syllables="kh-ae")
        self.assertIn("'", rec["result"])
    def test_simultaneous_rotation(self):
        rec = encode("rdbgt", consonant_map="rotation")
        self.assertEqual(rec["result"], "drgtb")
        self.assertEqual(decode(rec), "rdbgt")
    def test_rotation_with_syllable_inversion(self):
        rec = encode("gamarjoba", syllables="ga-mar-jo-ba", consonant_map="rotation")
        self.assertEqual(decode(rec), "gamarjoba")
        self.assertEqual(rec["consonant_map"], "rotation")
    def test_invalid_segmentation(self):
        with self.assertRaises(ValueError):
            encode("gamarjoba", syllables="ga-mar-ba")
    def test_tamper_detected(self):
        rec = encode("mtsvane", syllables="mtsva-ne")
        rec["result"] = "fake"
        with self.assertRaises(ValueError):
            decode(rec)

if __name__ == "__main__":
    unittest.main()
