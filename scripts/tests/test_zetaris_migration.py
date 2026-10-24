import csv
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('migration', ROOT / 'scripts/package_zetaris_migration.py')
migration = importlib.util.module_from_spec(spec)
spec.loader.exec_module(migration)

def fixtures(path):
    for filename in migration.DATA_FILES:
        output = io.StringIO(newline='')
        writer = csv.writer(output)
        if filename == 'travel-records.csv':
            writer.writerow(migration.HEADERS)
            for dataset in ('poi', 'catalog', 'map_layers', 'metadata'):
                identity = 'overture:example' if dataset == 'poi' else dataset
                payload = {'id': identity, 'name': dataset, 'lat': -33.8, 'lng': 151.2}
                writer.writerow([dataset, identity, dataset, 'food', -33.8, 151.2, 'public', '2026-10-24T00:00:00Z', json.dumps(payload)])
        else:
            writer.writerow(migration.PLACE_HEADERS)
            identity = 'gem:' + 'a' * 24 if filename == 'hidden-gems.csv' else 'osm:node:123'
            payload = {'id': identity, 'lat': -33.8, 'lng': 151.2, 'guide': {'sourceUrl': 'https://en.wikivoyage.org/'}, 'sourceLicense': 'ODbL 1.0'}
            writer.writerow([identity, 'Public place', -33.8, 151.2, json.dumps(payload)])
        (path / filename).write_text(output.getvalue())

class MigrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / 'source'
        self.source.mkdir()
        fixtures(self.source)
        self.archive = self.root / 'bundle.zip'

    def build(self):
        return migration.build(self.source, self.archive)

    def test_build_verify_and_allowlist_excludes_secrets(self):
        (self.source / '.env.local').write_text('PASSWORD=never-export')
        (self.source / 'users.json').write_text('private')
        manifest = self.build()
        self.assertEqual(manifest['counts']['poi'], 1)
        self.assertEqual(migration.verify(self.archive), manifest)
        with zipfile.ZipFile(self.archive) as archive:
            self.assertEqual(set(archive.namelist()), migration.ALLOWED | {'manifest.json'})
            self.assertFalse(any(b'never-export' in archive.read(name) for name in archive.namelist()))
            archive.extractall(self.root / 'extracted')
        self.assertEqual(migration.verify(self.root / 'extracted'), manifest)

    def test_existing_output_is_not_overwritten(self):
        self.build()
        before = self.archive.read_bytes()
        with self.assertRaises(FileExistsError): self.build()
        self.assertEqual(self.archive.read_bytes(), before)

    def test_missing_source_is_rejected(self):
        (self.source / 'hidden-gems.csv').unlink()
        with self.assertRaises(FileNotFoundError): self.build()
        self.assertFalse(self.archive.exists())

    def test_private_payload_is_rejected(self):
        path = self.source / 'hidden-gems.csv'
        path.write_text(path.read_text().replace('""sourceLicense""', '""password""'))
        with self.assertRaisesRegex(ValueError, 'Private field'): self.build()

    def test_weather_snapshot_is_rejected(self):
        path = self.source / 'travel-records.csv'
        path.write_text(path.read_text().replace('catalog,catalog,', 'weather,catalog,'))
        with self.assertRaisesRegex(ValueError, 'no weather'): self.build()

    def test_checksum_tampering_is_rejected(self):
        self.build()
        with zipfile.ZipFile(self.archive) as archive: archive.extractall(self.root / 'extracted')
        (self.root / 'extracted/restore.sql').write_text('SELECT 42;')
        with self.assertRaisesRegex(ValueError, 'Checksum'): migration.verify(self.root / 'extracted')

    def test_zip_traversal_is_rejected(self):
        with zipfile.ZipFile(self.archive, 'w') as archive: archive.writestr('../escape', 'bad')
        with self.assertRaisesRegex(ValueError, 'paths'): migration.verify(self.archive)

    def test_duplicate_zip_names_are_rejected(self):
        self.build()
        import warnings
        with warnings.catch_warnings():
            warnings.simplefilter('ignore')
            with zipfile.ZipFile(self.archive, 'a') as archive: archive.writestr('manifest.json', '{}')
        with self.assertRaises(ValueError): migration.verify(self.archive)

    def test_source_symlink_is_rejected(self):
        path = self.source / 'hidden-gems.csv'
        saved = self.root / 'saved.csv'
        path.rename(saved)
        path.symlink_to(saved)
        with self.assertRaisesRegex(ValueError, 'symlink'): self.build()

if __name__ == '__main__': unittest.main()
