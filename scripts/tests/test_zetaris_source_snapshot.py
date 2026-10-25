import base64,copy,hashlib,importlib.util,json,sys,tempfile,unittest,zipfile
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import zetaris_source_snapshot as m
class SnapshotTests(unittest.TestCase):
    def doc(self):return {'schemaVersion':1,'tables':{t:[] for t in m.TABLES}}
    def test_copy_payload_cannot_inject_sql_or_terminator(self):
        doc=self.doc();row={'id':'social:test','payload':json.dumps({'name':"x\n\\.\nDROP DATABASE danger; -- '"})};doc['tables']['social_place_records']=[row]
        sql=m.restore_sql(doc);self.assertNotIn('DROP DATABASE danger',sql)
        encoded=base64.b64encode(json.dumps(row,ensure_ascii=False).encode()).decode();self.assertIn(encoded,sql)
    def test_all_nine_views_and_no_weather(self):
        sql=m.restore_sql(self.doc(),'travel_reader');self.assertEqual(sql.count('CREATE VIEW'),9);self.assertNotIn('travel_weather',sql);self.assertIn('travel_activity_offers',sql)
    def test_rejects_private_fields_and_bad_reader(self):
        doc=self.doc();doc['tables']['social_place_records']=[{'id':'social:test','payload':'{"password":"never export"}'}]
        with self.assertRaises(ValueError):m.check(doc)
        with self.assertRaises(ValueError):m.restore_sql(self.doc(),'role;DROP')
    def test_weather_and_unexpected_columns_rejected(self):
        doc=self.doc();doc['tables']['travel_records']=[dict.fromkeys(m.TABLES['travel_records'],'')];doc['tables']['travel_records'][0]['dataset']='weather'
        with self.assertRaises(ValueError):m.check(doc)
        doc=self.doc();doc['tables']['social_place_records']=[{'id':'x','payload':'{}','extra':'bad'}]
        with self.assertRaises(ValueError):m.check(doc)
    def test_tampered_archive_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            file=Path(d)/'test.zip';raw=json.dumps(self.doc()).encode()
            with zipfile.ZipFile(file,'w') as z:z.writestr('snapshot.json',raw);z.writestr('manifest.json',json.dumps({'sha256':'wrong'}))
            with self.assertRaises(ValueError):m.read_snapshot(file)
if __name__=='__main__':unittest.main()
