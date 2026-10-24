import importlib.util
from pathlib import Path
import unittest
spec=importlib.util.spec_from_file_location('hidden_gems',Path(__file__).parents[1]/'import_hidden_gems.py');gems=importlib.util.module_from_spec(spec);spec.loader.exec_module(gems)
class HiddenGemsTests(unittest.TestCase):
    def listing(self,text):return list(gems.listings({'parse':{'revid':123,'wikitext':{'*':text}}},'Sydney/Lower_North_Shore','now'))
    def test_real_source_coordinate_directions_and_no_invented_reviews(self):
        text='{{see|name=Berry Island Reserve|lat=-33.8395|long=151.1889|content=Public [[coastal walk|walk]]|directions=From the public station|wikidata=Q1}}'
        p=self.listing(text)[0];self.assertEqual(p['description'],'Public walk');self.assertEqual(p['guide']['directions'],'From the public station');self.assertIn('oldid=123',p['guide']['sourceUrl']);self.assertEqual(p['reviews'],[]);self.assertEqual(p['accessStatus'],'unverified')
    def test_invalid_coordinates_and_non_selected_places_not_imported(self):
        self.assertEqual(self.listing('{{see|name=Berry Island Reserve|lat=nan|long=151}}'),[])
        self.assertEqual(self.listing('{{see|name=Private home|lat=-33.8|long=151.2}}'),[])
    def test_per_file_author_and_license_required(self):
        def response(license='CC BY-SA 4.0',artist='Photographer'):
            return {'query':{'pages':{'1':{'title':'File:Berry Island.jpg','imageinfo':[{'mime':'image/jpeg','thumburl':'https://thumb.wikimedia.org/example.jpg','descriptionurl':'https://commons.wikimedia.org/wiki/File:Berry_Island.jpg','extmetadata':{'LicenseShortName':{'value':license},'LicenseUrl':{'value':'https://creativecommons.org/licenses/by-sa/4.0/'},'Artist':{'value':artist}}}]}}}}
        photo=gems.photo_records(response(),'nearby-unverified')[0];self.assertEqual(photo['artist'],'Photographer');self.assertEqual(photo['matchKind'],'nearby-unverified')
        self.assertEqual(gems.photo_records(response('CC BY-NC 4.0'),'guide-file'),[]);self.assertEqual(gems.photo_records(response(artist=''),'guide-file'),[])
    def test_nearby_photo_requires_more_than_a_shared_surname(self):
        point={"name":"Wendy Whiteley Secret Garden"}
        photos=[{"title":"Brett Whiteleys House.jpg"},{"title":"Wendy Whiteley Secret Garden pano.jpg"}]
        self.assertEqual(gems.match_nearby_photos(point,photos),[photos[1]])
    def test_nested_templates_do_not_split_listing_fields(self):
        p=self.listing('{{see|name=Berry Island Reserve|lat=-33.8395|long=151.1889|content=Park|directions=Walk via {{rint|sydney|t1}} station}}')[0]
        self.assertEqual(p['guide']['directions'],'Walk via station');self.assertNotIn('rating',p)
if __name__=='__main__':unittest.main()
