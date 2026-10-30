"""Regression checks for malformed metadata, addresses and section boundaries."""
import tempfile
from pathlib import Path
import unittest

import lore_format as lore


class LoreFormatTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.original = (lore.DOCS / 'race_deymi.md').read_text(encoding='utf-8')

    def validate(self, body):
        # The fixture needs only local anchors; the pilot has no external file links.
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'article.md'
            path.write_text(body, encoding='utf-8')
            return lore.validate_article(path)

    def test_pilot_is_valid(self):
        self.assertEqual(self.validate(self.original), [])

    def test_duplicate_metadata_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'duplicate metadata'):
            lore.metadata('---\nid: "race.a"\nid: "race.b"\n---\n')

    def test_yaml_type_coercion_is_not_accepted(self):
        with self.assertRaises(ValueError):
            lore.metadata('---\nstatus: true\n---\n')

    def test_duplicate_anchor_is_rejected(self):
        damaged = self.original.replace('id="biology.reproduction"', 'id="overview"')
        self.assertTrue(any('unique explicit anchor' in e for e in self.validate(damaged)))

    def test_broken_section_reference_is_rejected(self):
        self.assertTrue(any('broken section link' in e for e in self.validate(
            self.original + '\n[missing](#not-a-section)\n')))

    def test_unknown_status_is_rejected(self):
        damaged = self.original.replace('section-status: filled', 'section-status: complete', 1)
        self.assertTrue(any('section status' in e for e in self.validate(damaged)))

    def test_required_slot_cannot_silently_disappear(self):
        damaged = self.original.replace('### III.12. Система размножения', '### Размножение')
        self.assertTrue(any('missing section slot: III.12' in e for e in self.validate(damaged)))

    def test_fixed_id_cannot_change_with_title(self):
        damaged = self.original.replace('id="biology.reproduction"', 'id="biology.eggs"')
        self.assertTrue(any('expected stable id biology.reproduction' in e for e in self.validate(damaged)))

    def test_section_bounds_exclude_next_section(self):
        sections = lore.parse_sections(self.original, markdown=True)
        item = next(s for s in sections if s['id'] == 'biology.reproduction')
        body = '\n'.join(self.original.splitlines()[item['line'] - 1:item['end_line']])
        self.assertIn('Яйцекладущие', body)
        self.assertNotIn('III.13.', body)
        parent = next(s for s in sections if s['id'] == 'biology')
        self.assertLess(parent['line'], item['line'])
        self.assertGreater(parent['end_line'], item['end_line'])

    def test_specific_legacy_section_beats_a_group_range(self):
        doc = {'sections': [
            {'id': 'group.x-5-x-17', 'slots': ['X.5', 'X.6']},
            {'id': 'society.kinship', 'slots': ['X.5']},
        ]}
        self.assertEqual(lore.select_sections(doc, 'X.5')[0]['id'], 'society.kinship')
        self.assertEqual(lore.select_sections(doc, 'society.kinship')[0]['slots'], ['X.5'])

    def test_range_closure_covers_all_three_gameplay_slots(self):
        sections = lore.parse_sections(self.original, markdown=True)
        gameplay = next(s for s in sections if s['id'] == 'gameplay')
        self.assertEqual(gameplay['slots'], ['XIV', 'XV', 'XVI'])
        self.assertEqual(gameplay['status'], 'needs_development')


if __name__ == '__main__':
    unittest.main()
