import unittest
from synapt_phrase import transform, convert, cyrillic

class SynaptPhraseTests(unittest.TestCase):
    def test_hyphens_are_hidden_by_default(self):
        word, _ = transform("gamarjoba")
        self.assertNotIn("-", word)
    def test_previous_variant_can_be_reproduced(self):
        word, _ = transform("gamarjoba", compact=False, hyphens=True)
        self.assertIn("-", word)
    def test_short_word_changes_without_vowel_deletion(self):
        word, _ = transform("da")
        self.assertEqual(word, "ta")
    def test_cyrillic_transcription(self):
        self.assertEqual(cyrillic("tshkh"), "цшх")
    def test_georgian_phrase_output(self):
        record = convert("ჩვენ ცოდნას ვეძებთ")
        self.assertIn("georgian_ru", record)
        self.assertIn("synapt_ru", record)
        self.assertIn("previous_ru", record)
        self.assertNotIn("-", record["synapt_ru"])

if __name__ == "__main__":
    unittest.main()
