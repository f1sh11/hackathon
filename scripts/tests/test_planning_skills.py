import importlib.util,json,tempfile,unittest
from pathlib import Path
spec=importlib.util.spec_from_file_location('skills',Path(__file__).parents[1]/'planning_skills.py');skills=importlib.util.module_from_spec(spec);spec.loader.exec_module(skills)
class SkillTests(unittest.TestCase):
 def test_append_only_bilingual_build_and_version_guard(self):
  with tempfile.TemporaryDirectory() as d:
   p=Path(d);(p/'version.json').write_text(json.dumps({'version':'test.1','baseVersion':skills.BASE}))
   for language in ['en','zh-Hant']:
    original=(skills.ROOT/'server-dotnet/SydneyBuddy.Api/Planning/Skills'/f'itinerary.{language}.md').read_text()
    (p/f'itinerary.{language}.md').write_text(original+'\nAdditional rule.')
   self.assertEqual('test.1',json.loads(skills.build(p))['version'])
   (p/'itinerary.en.md').write_text('Replacement only')
   with self.assertRaises(ValueError):skills.build(p)
 def test_empty_appendix_is_rejected(self):
  with tempfile.TemporaryDirectory() as d:
   p=Path(d);(p/'version.json').write_text(json.dumps({'version':'test.1','baseVersion':skills.BASE}))
   for language in ['en','zh-Hant']:
    (p/f'itinerary.{language}.md').write_text((skills.ROOT/'server-dotnet/SydneyBuddy.Api/Planning/Skills'/f'itinerary.{language}.md').read_text())
   with self.assertRaises(ValueError):skills.build(p)
if __name__=='__main__':unittest.main()
