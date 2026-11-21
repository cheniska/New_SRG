import unittest

from phonotactics import Syllable, check_syllable, check_word, generate, render


class PhonotacticsTests(unittest.TestCase):
    def test_reproducible(self):
        self.assertEqual(generate(100, seed=42), generate(100, seed=42))

    def test_unique(self):
        words = generate(100, seed=42)
        self.assertEqual(len(words), len({x["form"] for x in words}))

    def test_every_generated_word_is_valid(self):
        for item in generate(150, seed=99):
            syllables = [
                Syllable(tuple(s["onset"]), s["nucleus"], tuple(s["coda"]))
                for s in item["syllables"]
            ]
            self.assertEqual(check_word(syllables), [])
            self.assertEqual(render(syllables), item["form"])

    def test_syllabic_sonorants(self):
        self.assertEqual(render([Syllable(("к",), "R", ("н",))]), "крн")
        self.assertEqual(render([Syllable(("т",), "N", ("к",))]), "тнк")

    def test_invalid_onset(self):
        self.assertIn("unknown onset",
                      check_syllable(Syllable(("з", "к", "т"), "а", ())))

    def test_no_affricate_collision(self):
        syllables = [Syllable(("м",), "а", ("ц",)),
                     Syllable(("ц",), "и", ())]
        self.assertIn("two adjacent affricates", check_word(syllables))

    def test_modes_differ(self):
        self.assertNotEqual(
            [x["form"] for x in generate(25, seed=42, style="chitter")],
            [x["form"] for x in generate(25, seed=42, style="balanced")]
        )

    def test_bad_parameters(self):
        with self.assertRaises(ValueError):
            generate(0)
        with self.assertRaises(ValueError):
            generate(5, min_syllables=3, max_syllables=2)


if __name__ == "__main__":
    unittest.main()
