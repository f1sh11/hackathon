import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

def module(name):
    spec=importlib.util.spec_from_file_location(name,Path(__file__).parents[1]/(name+'.py'));mod=importlib.util.module_from_spec(spec);spec.loader.exec_module(mod);return mod
prepare=module('prepare_zetaris_data');verify=module('check_zetaris_local')
class ZetarisDataTests(unittest.TestCase):
    def snapshot(self):return {'schemaVersion':1,'source':'Overture Maps','release':'2026-10-21.0','downloadedAt':'2026-10-24T00:00:00Z','places':[{'id':'overture:test','source':'Overture Maps','name':'Cafe, "A"\nSydney','searchCategory':'food','lat':-33.86,'lng':151.21}]}
    def weather(self):return {'timezone':'Australia/Sydney','daily':{'time':['2026-10-24','2026-10-25'],'weather_code':[61,None],'temperature_2m_max':[22,25],'temperature_2m_min':[15,16],'precipitation_probability_max':[80,None]}}
    def test_only_public_sources_and_csv_json_round_trip(self):
        import csv
        snapshot=self.snapshot();rows=prepare.build_rows(snapshot,[])
        with tempfile.TemporaryDirectory() as tmp:
            manifest=prepare.write_export(rows,Path(tmp))
            with (Path(tmp)/'travel-records.csv').open() as stream:stored=list(csv.DictReader(stream))
            self.assertEqual(json.loads(stored[0]['payload'])['name'],snapshot['places'][0]['name']);self.assertEqual(manifest['counts']['poi'],1);self.assertFalse(manifest['zetarisVerified'])
        snapshot['places'][0]['source']='Google Places'
        with self.assertRaises(ValueError):prepare.build_rows(snapshot,[])
    def test_weather_is_no_longer_exported(self):
        with self.assertRaises(ValueError):prepare.build_rows(self.snapshot(),[],[{"date":"2026-10-24"}])
        self.assertNotIn("weather",{r["dataset"] for r in prepare.build_rows(self.snapshot(),[])})
    def test_weather_unknown_is_not_sunny_and_has_provenance(self):
        weather=prepare.weather_records(self.weather(),'2026-10-24T00:00:00Z');self.assertTrue(weather[0]['rainy']);self.assertFalse(weather[1]['available']);self.assertIsNone(weather[1]['precipitationProbability']);self.assertEqual(weather[0]['source'],'Open-Meteo')
        raw=self.weather();raw['daily']['weather_code']=[]
        with self.assertRaises(ValueError):prepare.weather_records(raw,'now')
    def test_duplicate_and_invalid_points_rejected(self):
        raw=self.snapshot();raw['places']*=2
        with self.assertRaises(ValueError):prepare.build_rows(raw,[])
        raw=self.snapshot();raw['places'][0]['lat']=float('nan')
        with self.assertRaises(ValueError):prepare.build_rows(raw,[])
    def test_local_verifier_refuses_remote_credentials_transmission(self):
        with self.assertRaises(ValueError):verify.check('https://remote.example/api/v1.0','1','not-a-real-key')
        with self.assertRaises(ValueError):verify.check('http://localhost:8888/api/v1.0','bad','not-a-real-key')
    def test_read_only_contract_and_counts(self):
        class Response:
            def __init__(self,value):self.value=value
            def __enter__(self):return self
            def __exit__(self,*args):pass
            def read(self):return json.dumps(self.value).encode()
        class FakeTransport:
            def __init__(self):self.sql=[]
            def open(self,request,timeout):
                if request.data is None:return Response([])
                sql=json.loads(request.data)['sql'];self.sql.append(sql)
                return Response({'data':[['1']] if sql=='SELECT 1' else [] if sql.startswith('SHOW') else [['4']]})
        transport=FakeTransport();result=verify.check('http://localhost:8888/api/v1.0','1','test-only',opener=transport)
        self.assertEqual(result['counts']['poi'],4);self.assertTrue(all(s.startswith(('SELECT','SHOW')) for s in transport.sql))
        with self.assertRaises(ValueError):verify.check('http://localhost:8888/api/v1.0','1','test-only',{'counts':{'poi':5}},transport)
if __name__=='__main__':unittest.main()
