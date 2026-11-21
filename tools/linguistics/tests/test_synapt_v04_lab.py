import unittest
from synapt_v04_lab import transform, batch, validate

class SynaptV04Tests(unittest.TestCase):
    def test_context_change(self):
        self.assertEqual(transform("sava","A")["latin"],"sawa")
        self.assertEqual(transform("vra","A")["latin"],"vra")
    def test_final_vowel_variant(self):
        self.assertEqual(transform("marta","A")["latin"],"marta")
        self.assertEqual(transform("marta","B")["latin"],"marte")
    def test_reproducible_trace(self):
        self.assertEqual(transform("sava","A"),transform("sava","A"))
        self.assertEqual(transform("sava","A")["trace"][0]["rule"],"intervocalic_v")
    def test_short_words_survive(self):
        self.assertEqual(transform("da","B")["latin"],"de")
        self.assertEqual(transform("tsnoba","A")["latin"],"tsnoba")
    def test_batch(self):
        result=batch(["sava","sava"])
        self.assertTrue(result["rows"][0]["A"]["collision"])
    def test_invalid_input(self):
        with self.assertRaises(ValueError):
            transform("мцквар")
    def test_clusters(self):
        self.assertEqual(validate("mtskvar"),[])

if __name__=="__main__":
    unittest.main()
