import unittest
from morphology_v2 import noun, verb, lexeme, sentence, survey, NOUN_NUMBER, NOUN_CASE, LEXICAL_DERIVATION, ROOTS

class MorphologyV2Tests(unittest.TestCase):
    def test_one_root_inflection(self):
        self.assertEqual(noun("detect")["form"], "дзрим")
        self.assertEqual(noun("detect",number="pl",case="acc")["form"], "дзримкэс")
    def test_derivation_precedes_inflection(self):
        result=noun("detect",derived="instrument",number="pl",case="loc")
        self.assertEqual(result["form"],"дзримацкэц")
        self.assertEqual(result["morphemes"],["дзрим","ац","к","эц"])
    def test_compound(self):
        self.assertEqual(noun("signal","detect",derived="instrument")["form"],"цвэкидзримац")
        self.assertNotEqual(noun("detect","signal")["form"],noun("signal","detect")["form"])
    def test_prefixes_and_tense(self):
        self.assertEqual(verb("detect",tense="pst",polarity="neg")["form"],"шэдзримит")
        self.assertEqual(verb("detect",tense="fut",aspect="ongoing")["form"],"мэдзримав")
    def test_sentence(self):
        self.assertEqual(sentence("person","detect","signal",tense="pst")["text"],
                         "мцэр цвэкэс дзримит")
    def test_no_collisions(self):
        self.assertEqual(survey()["collisions"], {})
    def test_expanded_cases(self):
        self.assertEqual(noun("place", case="abl")["form"], "цхэмаш")
        self.assertEqual(noun("place", case="all")["form"], "цхэмуц")
        self.assertEqual(noun("person", case="com")["form"], "мцэрил")
    def test_collective_and_mood(self):
        self.assertEqual(noun("detect", number="collective")["form"], "дзримум")
        self.assertEqual(verb("detect", tense="fut")["form"], "дзримав")
        self.assertEqual(verb("detect", mood="cond", tense="pst")["form"], "дзримиташ")
    def test_survey_count(self):
        self.assertEqual(survey()["nominal_forms"], len(ROOTS)*len(LEXICAL_DERIVATION)*len(NOUN_NUMBER)*len(NOUN_CASE))
    def test_unknown_features_fail(self):
        with self.assertRaises(ValueError): noun("detect",case="unknown")
        with self.assertRaises(ValueError): verb("detect",tense="unknown")
        with self.assertRaises(ValueError): lexeme("missing")

if __name__=="__main__":
    unittest.main()
