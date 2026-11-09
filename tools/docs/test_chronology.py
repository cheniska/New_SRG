"""Regression checks for semantic date identity and controlled propagation."""
import copy
import json
from pathlib import Path
import tempfile
import unittest
import chronology


class ChronologyTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        self.root=Path(self.tmp.name); docs=self.root/'GameDoc';(docs/'data').mkdir(parents=True)
        self.data={'scope':'test','events':[{'id':'contact.test','date':'2722','title':'Contact',
                    'source':'race_test.md#history','precision':'exact'}],'undated':[]}
        self.file=docs/'race_test.md'
        self.file.write_text('<a id="history"></a>\n2722<!-- date:contact.test -->; unrelated 2720\n')
        self.save();chronology.process(self.root,True)

    def save(self):
        (self.root/'GameDoc/data/chronology.json').write_text(json.dumps(self.data))

    def test_wrong_date_fails_and_sync_updates_only_bound_date(self):
        self.data['events'][0]['date']='2723';self.save()
        self.assertTrue(chronology.process(self.root)[0])
        self.assertEqual(chronology.process(self.root,True)[0],[])
        self.assertIn('2723<!-- date:contact.test -->; unrelated 2720',self.file.read_text())
        self.assertEqual(chronology.process(self.root)[0],[])

    def test_unknown_or_malformed_binding_fails(self):
        for text in ['2722<!-- date:unknown -->','no date<!-- date:contact.test -->']:
            with self.subTest(text=text):
                self.file.write_text('<a id="history"></a>\n'+text)
                self.assertTrue(chronology.process(self.root)[0])

    def test_missing_binding_fails(self):
        self.file.write_text('<a id="history"></a>\n2722')
        self.assertIn('Unbound event: contact.test',chronology.process(self.root)[0])

    def test_stale_readable_registry_fails(self):
        (self.root/'GameDoc/reference_chronology.md').write_text('stale')
        self.assertTrue(chronology.process(self.root)[0])

    def test_duplicate_event_or_bad_source_fails(self):
        self.data['events'].append(copy.deepcopy(self.data['events'][0]));self.save()
        with self.assertRaises(ValueError): chronology.process(self.root)
        self.data['events'].pop();self.data['events'][0]['source']='missing.md#history';self.save()
        self.assertTrue(chronology.process(self.root)[0])


if __name__=='__main__': unittest.main()
