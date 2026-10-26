"""Regression checks for the English catalog gate borrowed from the English rollout."""
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

SOURCE = Path(__file__).resolve().parents[1] / 'generate_localizations.py'

class EnglishLocalizationGuardTests(unittest.TestCase):
    def run_catalog(self, key, english, check=False):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'scripts').mkdir()
            (root / 'shared/localization').mkdir(parents=True)
            shutil.copyfile(SOURCE, root / 'scripts/generate_localizations.py')
            (root / 'shared/localization/ui.json').write_text(json.dumps({key: {'en': english, 'zh-Hans': key, 'zh-Hant': key}}))
            command = [sys.executable, str(root / 'scripts/generate_localizations.py'), '--ios-only']
            result = subprocess.run(command, capture_output=True, text=True)
            if check and result.returncode == 0:
                result = subprocess.run(command + ['--check'], capture_output=True, text=True)
            return result

    def test_rejects_chinese_in_english_copy(self):
        result = self.run_catalog('旅行', '旅行')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('English translation contains Chinese copy', result.stderr)

    def test_rejects_lost_money_format(self):
        result = self.run_catalog(' %.2f / 人', ' $0.00 / person')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Invalid printf placeholders', result.stderr)

    def test_rejects_changed_numbered_placeholder(self):
        result = self.run_catalog('第 {0} 天', 'Day {1}')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Invalid placeholders', result.stderr)

    def test_valid_copy_generates_and_checks(self):
        result = self.run_catalog('第 {0} 天 · %.2f', 'Day {0} · %.2f', check=True)
        self.assertEqual(result.returncode, 0, result.stderr)

if __name__ == '__main__':
    unittest.main()
