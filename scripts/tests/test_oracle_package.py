import importlib.util
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest

SCRIPTS=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(SCRIPTS))
from package_oracle_backend import package

class OraclePackageTests(unittest.TestCase):
    def test_release_contains_embedded_prompts_without_private_configuration(self):
        with tempfile.TemporaryDirectory() as directory:
            target=Path(directory)/'release.tar.gz'
            package(target)
            with tarfile.open(target) as archive:
                names=archive.getnames()
                for name in ('Planning/Skills/itinerary.en.md','Planning/Skills/itinerary.zh-Hant.md','Prompts/PersonalProfile.md'):
                    source='server-dotnet/SydneyBuddy.Api/'+name
                    self.assertIn(source,names)
                    self.assertEqual(archive.extractfile(source).read(),(SCRIPTS.parent/source).read_bytes())
                self.assertFalse(any(Path(n).name in ('.secrets.json','.env') or '.hmem/' in n or '.pet-sessions/' in n for n in names))
