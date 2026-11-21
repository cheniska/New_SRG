import unittest
from sentence_trials import trials


class SentenceTrialsTests(unittest.TestCase):
    def test_ten_utterances(self):
        self.assertEqual(len(trials()), 10)

    def test_word_order(self):
        for item in trials():
            self.assertEqual(len(item["words"]), 3)
            self.assertEqual(item["text"], " ".join(w["form"] for w in item["words"]))
            self.assertEqual(item["words"][0]["tags"]["pos"], "noun")
            self.assertEqual(item["words"][1]["tags"]["case"], "acc")
            self.assertEqual(item["words"][2]["tags"]["pos"], "verb")

    def test_example_present(self):
        self.assertEqual(trials()[0]["text"], "мцэр цвэкэс дзрима")

    def test_compound_is_one_word(self):
        t = trials()[-1]
        self.assertEqual(len(t["text"].split(" ")), 3)
        self.assertEqual(t["words"][1]["tags"]["roots"], "signal+detect")
        self.assertEqual(t["words"][1]["tags"]["derived"], "instrument")

    def test_detection_device_single_root(self):
        self.assertEqual(trials()[7]["words"][1]["tags"]["derived"], "instrument")


if __name__ == "__main__":
    unittest.main()
