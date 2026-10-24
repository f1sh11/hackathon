import importlib.util,unittest
from pathlib import Path
spec=importlib.util.spec_from_file_location('osm_map',Path(__file__).parents[1]/'import_osm_map_places.py');osm=importlib.util.module_from_spec(spec);spec.loader.exec_module(osm)
class OsmMapTests(unittest.TestCase):
    def point(self,i,**tags):return {'id':i,'type':'node','lat':-33.86,'lon':151.21,'tags':dict(name='Public '+str(i),**tags)}
    def test_all_supported_kinds_are_separate_and_private_points_excluded(self):
        raw={'elements':[self.point(1,tourism='hotel'),self.point(2,shop='books'),self.point(3,amenity='restaurant'),self.point(4,tourism='viewpoint'),self.point(5,waterway='waterfall'),self.point(6,tourism='hotel',access='private')]}
        points=osm.normalize(raw,osm.BBOX,'now')['places'];self.assertEqual(len(points),5);self.assertEqual([p['searchCategory'] for p in points],['lodging','shops','food','gems','gems']);self.assertTrue(all(p['sourceUrl'].startswith('https://www.openstreetmap.org/node/') for p in points))
    def test_incomplete_or_out_of_area_data_cannot_replace_import(self):
        with self.assertRaises(ValueError):osm.normalize({'remark':'timeout'},osm.BBOX,'now')
        point=self.point(1,tourism='hotel');point['lat']=0
        self.assertEqual(osm.normalize({'elements':[point]},osm.BBOX,'now')['places'],[])
    def test_query_uses_real_osm_waterfall_tag_and_includes_hotels_and_shops(self):
        sql=osm.query(osm.BBOX);self.assertIn('[waterway=waterfall]',sql);self.assertIn('hotel|hostel|motel|guest_house',sql);self.assertIn('[shop]',sql)
if __name__=='__main__':unittest.main()
