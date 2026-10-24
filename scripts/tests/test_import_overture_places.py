import importlib.util
from pathlib import Path
import unittest

spec=importlib.util.spec_from_file_location('importer',Path(__file__).resolve().parents[1]/'import_overture_places.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)

class ImportTests(unittest.TestCase):
    def feature(self,kind='cafe',confidence=.9):
        return {'id':'00000000-0000-4000-8000-000000000001','geometry':{'type':'Point','coordinates':[151.21,-33.86]},
                'properties':{'names':{'primary':'Test Café'},'confidence':confidence,'taxonomy':{'primary':kind,'hierarchy':['food_and_drink',kind] if kind=='cafe' else [kind]},'websites':['https://example.com'],'addresses':[{'locality':'Sydney'}],'sources':[{'license':'CDLA-Permissive-2.0'}]}}
    def test_import_preserves_real_fields_and_never_invents_hours_or_price(self):
        data=module.normalize({'features':[self.feature()]},module.DEFAULT_BBOX,'2026-10-21.0')
        point=data['places'][0];self.assertEqual('food',point['searchCategory']);self.assertEqual('https://example.com',point['website']);self.assertEqual('Sydney',point['address']);self.assertEqual(0,point['cost']);self.assertNotIn('openMinute',point)
    def test_drops_low_confidence_closed_outside_and_bad_geometry(self):
        low=self.feature(confidence=.3);closed=self.feature();closed['properties']['operating_status']='permanently_closed'
        outside=self.feature();outside['geometry']['coordinates']=[150,-33]
        bad=self.feature();bad['geometry']['coordinates']=[float('nan'),-33.86]
        self.assertEqual([],module.normalize({'features':[low,closed,outside,bad]},module.DEFAULT_BBOX,'2026-10-21.0')['places'])
    def test_parking_is_not_a_park_and_only_facility_taxonomy_becomes_toilet(self):
        self.assertIsNone(module.classification({'taxonomy':{'primary':'parking','hierarchy':['travel_and_transportation','parking']}}))
        self.assertEqual(('sights','park'),module.classification({'taxonomy':{'primary':'park','hierarchy':['park']}}))
        self.assertEqual(('facilities','toilets'),module.classification({'taxonomy':{'primary':'public_restroom','hierarchy':['public_facility','public_restroom']}}))

if __name__=='__main__':unittest.main()
