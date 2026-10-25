import copy
import importlib.util
from pathlib import Path
import unittest
spec=importlib.util.spec_from_file_location('activity_sync',Path(__file__).parents[1]/'sync_activity_offers_zetaris.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)


class ActivityTests(unittest.TestCase):
    def input(self):
        return {'schemaVersion':1,'offers':[{'id':'synthetic','title':'Test tour','venueName':'Test venue','lat':-33.85,'lng':151.21,'amount':50,'currency':'AUD','ticketType':'Adult prebook','conditions':'Synthetic test reference','sourceName':'Fixture','sourceUrl':'https://example.com','observedOn':'2026-01-01','reviewBy':'2026-02-01','license':'test'}]}
    def test_always_reference_no_budget(self):
        raw=self.input();raw['offers'][0]['budgetEligible']=True
        self.assertFalse(module.validate(raw)[0]['budgetEligible'])
    def test_reject_duplicate_ids(self):
        raw=self.input();raw['offers'].append(copy.deepcopy(raw['offers'][0]))
        with self.assertRaises(ValueError):module.validate(raw)
    def test_reject_invalid_reference_fields(self):
        for key,value in [('currency',''),('amount',float('nan')),('lat',True),('sourceUrl','http://example.com'),('reviewBy','2025-01-01')]:
            raw=self.input();raw['offers'][0][key]=value
            with self.assertRaises(ValueError):module.validate(raw)
    def test_ignores_extra_private_fields(self):
        raw=self.input();raw['offers'][0]['cookies']='not exported'
        self.assertNotIn('cookies',module.validate(raw)[0])
