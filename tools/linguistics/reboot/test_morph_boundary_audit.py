import unittest
from morph_boundary_audit import inspect_boundary, boundary_report, audit

class BoundaryAuditTests(unittest.TestCase):
    def test_vowel_hiatus(self):
        self.assertIn("vowel_hiatus", inspect_boundary("дзрима", "эц"))
    def test_duplicate_consonant(self):
        self.assertIn("identical_consonants", inspect_boundary("цвэк", "к"))
    def test_valid_boundary(self):
        self.assertEqual(inspect_boundary("дзрим", "ац"), [])
    def test_affricate_collision(self):
        self.assertIn("adjacent_affricates", inspect_boundary("мц", "цра"))
    def test_trace(self):
        self.assertEqual(boundary_report(["цвэк","к"])[0]["context"],"цвэкк")
    def test_total_paradigms(self):
        result=audit()
        self.assertEqual(result["noun"]["forms"],3240)
        self.assertEqual(result["verb"]["forms"],2016)
        self.assertEqual(result["noun"]["distinct_surfaces"],3240)
        self.assertEqual(result["verb"]["distinct_surfaces"],2016)

if __name__=="__main__":
    unittest.main()
