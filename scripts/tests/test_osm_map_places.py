import importlib.util,unittest
from pathlib import Path
spec=importlib.util.spec_from_file_location('osm_map',Path(__file__).parents[1]/'import_osm_map_places.py');osm=importlib.util.module_from_spec(spec);spec.loader.exec_module(osm)
class OsmMapTests(unittest.TestCase):
    def point(self,i,**tags):return {'id':i,'type':'node','lat':-33.86,'lon':151.21,'tags':dict(name='Public '+str(i),**tags)}
    def test_all_supported_kinds_are_separate_and_private_points_excluded(self):
        raw={'elements':[self.point(1,tourism='hotel'),self.point(2,shop='books'),self.point(3,amenity='restaurant'),self.point(4,tourism='viewpoint'),self.point(5,waterway='waterfall'),self.point(6,tourism='hotel',access='private')]}
        points=osm.normalize(raw,osm.BBOX,'now')['places'];self.assertEqual(len(points),5);self.assertEqual([p['searchCategory'] for p in points],['lodging','shops','food','gems','gems']);self.assertTrue(all(p['sourceUrl'].startswith('https://www.openstreetmap.org/node/') for p in points))
    def test_regional_hotels_campsites_parks_and_nightlife_keep_categories(self):
        raw={'elements':[self.point(1,tourism='hostel'),self.point(2,tourism='caravan_site'),self.point(3,leisure='nature_reserve'),self.point(4,amenity='pub'),self.point(5,amenity='bar'),self.point(6,amenity='biergarten')]}
        points=osm.normalize(raw,osm.BBOX,'now')['places']
        self.assertEqual([p['searchCategory'] for p in points],['lodging','lodging','sights','food','food','food'])
        self.assertEqual([p['poiKind'] for p in points],['hostel','caravan_site','nature_reserve','pub','bar','biergarten'])
        sql=osm.query(osm.REGIONS['katoomba-leura']);self.assertIn('pub|bar|nightclub|biergarten',sql)
    def test_chinese_name_does_not_discard_english_source_fields(self):
        point=self.point(9,tourism='museum',**{'name:zh':'博物館','name:en':'Museum in English','description:zh':'中文介紹','description':'Original description'})
        record=osm.normalize({'elements':[point]},osm.BBOX,'now')['places'][0]
        self.assertEqual(record['name'],'博物館')
        self.assertEqual(record['nameEn'],'Museum in English')
        self.assertEqual(record['descriptionEn'],'Original description')
        self.assertEqual(record['description'],'中文介紹')
    def test_incomplete_or_out_of_area_data_cannot_replace_import(self):
        with self.assertRaises(ValueError):osm.normalize({'remark':'timeout'},osm.BBOX,'now')
        point=self.point(1,tourism='hotel');point['lat']=0
        self.assertEqual(osm.normalize({'elements':[point]},osm.BBOX,'now')['places'],[])
    def test_query_uses_real_osm_waterfall_tag_and_includes_hotels_and_shops(self):
        sql=osm.query(osm.BBOX);self.assertIn('[waterway=waterfall]',sql);self.assertIn('hotel|hostel|motel|guest_house',sql);self.assertIn('[shop]',sql)
if __name__=='__main__':unittest.main()
