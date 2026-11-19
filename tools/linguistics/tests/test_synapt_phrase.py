import unittest
from synapt_phrase import transform, convert, cyrillic

class SynaptPhraseTests(unittest.TestCase):
    def test_no_hyphens(self):
        result, _ = transform("gamarjoba")
        self.assertNotIn("-", result)

    def test_short_words_not_collapsed(self):
        result, _ = transform("da")
        self.assertEqual(result, "da")

    def test_cluster_preserved(self):
        result, _ = transform("mtsvane")
        self.assertTrue(result.startswith("mtsw"))

    def test_four_segments_local_swap(self):
        result, segments = transform("gamarjoba")
        self.assertEqual(len(segments), 4)
        self.assertEqual(result, "gamaobalj")

    def test_cyrillic(self):
        self.assertEqual(cyrillic("tshkh"), "цшх")

    def test_georgian_phrase(self):
        record = convert("ჩვენ ცოდნას ვეძებთ")
        self.assertIn("georgian_ru", record)
        self.assertIn("synapt_ru", record)
        self.assertNotIn("-", record["synapt_ru"])

if __name__ == "__main__":
    unittest.main()
