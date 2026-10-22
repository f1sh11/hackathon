"""Run against the local development server. Deletes only records created by this test."""
import json
import unittest
import urllib.request
import urllib.error

class APITests(unittest.TestCase):
 def call(self,path,body=None,method=None):
  r=urllib.request.Request('http://127.0.0.1:8787/'+path,data=None if body is None else json.dumps(body).encode(),headers={'Content-Type':'application/json'},method=method)
  with urllib.request.urlopen(r,timeout=15) as response:return json.load(response)
 def test_plan_save_revision_and_delete(self):
  trip=self.call('plan',{'mode':'demo','date':'2026-10-24','days':1,'budget':100,'maxSteps':10000,'startMinute':600,'endMinute':1080})
  saved=self.call('trips',{'trip':trip,'expectedRevision':0})
  try:
   self.assertEqual(saved['revision'],1)
   with self.assertRaises(urllib.error.HTTPError) as e:self.call('trips',{'trip':saved,'expectedRevision':0})
   self.assertEqual(e.exception.code,409)
   self.assertTrue(any(t['id']==saved['id'] for t in self.call('trips')))
  finally:self.call('trips/'+saved['id'],method='DELETE')
 def test_consent_required(self):
  with self.assertRaises(urllib.error.HTTPError) as e:self.call('memories',{'consent':False})
  self.assertEqual(e.exception.code,422)
 def test_hmem_save_retrieve_and_revoke(self):
  m=self.call('memories',{'consent':True,'summary':'travel preference Sydney art test-only','profile':{'interests':['艺术'],'maxSteps':5000}})
  try:
   self.assertTrue(any(x['id']==m['id'] for x in self.call('memories')))
   import sys
   from pathlib import Path
   sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
   from memory import run
   recalled=run('query',query='travel preference Sydney art test-only')
   self.assertIn(m['id'],[x['memory']['id'] for x in recalled['memories']])
  finally:self.call('memories/'+m['id'],method='DELETE')
  self.assertNotIn(m['id'],[x['memory']['id'] for x in run('query',query='travel preference Sydney art test-only')['memories']])
 def test_cross_origin_mutations_rejected(self):
  r=urllib.request.Request('http://127.0.0.1:8787/plan',data=b'{}',headers={'Origin':'https://example.com'})
  with self.assertRaises(urllib.error.HTTPError) as e:urllib.request.urlopen(r)
  self.assertEqual(e.exception.code,403)
if __name__=='__main__':unittest.main()
