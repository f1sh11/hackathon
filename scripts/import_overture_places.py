#!/usr/bin/env python3
"""Download real Overture Places for a bounded Sydney area and build the local search index.
Install scripts/requirements-open-poi.txt in a venv. Standard-library normalization can also
use --input from a prior official GeoJSON download. Never scrapes Google or generates POIs.
"""
import argparse
from collections import Counter
from datetime import datetime, timezone
import json
import math
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import uuid

DEFAULT_BBOX = (151.18, -33.90, 151.24, -33.84)
SIGHTS = {'museum','art_gallery','park','national_park','amusement_park','aquarium','zoo',
          'historic_site','monument','landmark','botanical_garden','tourist_attraction',
          'performing_arts_venue','theater','library','beach','observation_deck'}
FOOD = {'food_and_drink','restaurant','cafe','coffee_shop','bakery','bar','pub','casual_eatery'}
LODGING = {'accommodation','lodging','hotel','hostel','motel','guest_house','resort_hotel','bed_and_breakfast'}
FACILITIES = {'public_restroom':'toilets','drinking_water':'drinking_water','water_fountain':'drinking_water'}


def classification(properties):
    taxonomy = properties.get('taxonomy') or {}
    legacy = properties.get('categories') or {}
    terms = set(taxonomy.get('hierarchy') or [])
    terms.update(x for x in [taxonomy.get('primary'),properties.get('basic_category'),legacy.get('primary')] if x)
    for name, kind in FACILITIES.items():
        if name in terms: return 'facilities', kind
    if terms & LODGING:
        primary = taxonomy.get('primary') or legacy.get('primary') or 'hotel'
        if 'hostel' in terms or primary == 'hostel': return 'lodging', 'hostel'
        if 'motel' in terms or primary == 'motel': return 'lodging', 'motel'
        if 'guest_house' in terms or 'bed_and_breakfast' in terms: return 'lodging', 'guest_house'
        if 'resort_hotel' in terms or primary == 'resort_hotel': return 'lodging', 'resort_hotel'
        return 'lodging', 'hotel'
    if terms & FOOD: return 'food', taxonomy.get('primary') or legacy.get('primary') or 'restaurant'
    if terms & SIGHTS: return 'sights', taxonomy.get('primary') or legacy.get('primary') or 'attraction'
    return None


def normalize(document, bbox, release, minimum_confidence=.7):
    points = {}
    licenses = set()
    for feature in document.get('features', []):
        p = feature.get('properties') or {}
        classified = classification(p)
        if not classified or (p.get('confidence') or 0) < minimum_confidence: continue
        if p.get('operating_status') in {'closed','permanently_closed'}: continue
        geometry = feature.get('geometry') or {}
        if geometry.get('type') != 'Point': continue
        xy = geometry.get('coordinates') or []
        if len(xy) != 2 or not all(isinstance(v,(int,float)) and math.isfinite(v) for v in xy): continue
        lng, lat = xy
        if not (bbox[0] <= lng <= bbox[2] and bbox[1] <= lat <= bbox[3]): continue
        names = p.get('names') or {}
        name = names.get('primary') or ''
        if not name or len(name)>200: continue
        try: identifier = str(uuid.UUID(feature.get('id') or p.get('id')))
        except (ValueError,TypeError,AttributeError): continue
        category, kind = classified
        address_parts = []
        for address in (p.get('addresses') or [])[:1]:
            address_parts = [address.get(k) for k in ['freeform','locality','region','postcode'] if address.get(k)]
        address = ', '.join(address_parts)[:600]
        websites = [v for v in (p.get('websites') or []) if isinstance(v,str) and v.startswith(('https://','http://'))]
        phone = next((v for v in (p.get('phones') or []) if isinstance(v,str)), '')[:100]
        source_licenses = sorted({v['license'] for v in (p.get('sources') or []) if v.get('license')})
        licenses.update(source_licenses)
        description = '\n'.join(v for v in [kind.replace('_',' '), address, phone] if v)
        search_text = ' '.join([name, json.dumps(names.get('common') or {},ensure_ascii=False), address, kind.replace('_',' ')])
        points['overture:'+identifier] = {
            'id':'overture:'+identifier,'name':name,'lat':lat,'lng':lng,
            'source':'Overture Maps','sourceRelease':release,'sourceLicense':', '.join(source_licenses),
            'category':{'food':'美食','lodging':'住宿','facilities':'設施','sights':'風景'}[category],
            'searchCategory':category,'searchText':search_text,'poiKind':kind,
            'description':description,'address':address,'website':websites[0][:2000] if websites else '',
            'phone':phone,'indoor':False,'visitMinutes':{'food':45,'lodging':30,'facilities':10,'sights':60}[category],
            'cost':0,'reason':'由開放商家搜尋加入工具箱','openingHoursText':'',
            'note':'Overture 區域資料快照；分類、室內外、費用、營業時間與排隊尚未確認，出發前請查核。',
        }
    return {'schemaVersion':1,'source':'Overture Maps','release':release,
            'downloadedAt':datetime.now(timezone.utc).isoformat(),'bbox':list(bbox),
            'minimumConfidence':minimum_confidence,'licenses':sorted(licenses),
            'places':list(points.values())}


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bbox',default=','.join(map(str,DEFAULT_BBOX)))
    parser.add_argument('--release',help='Official release version; default: latest when downloading')
    parser.add_argument('--input',type=Path,help='Existing official Overture place GeoJSON; requires --release')
    parser.add_argument('--output',type=Path,default=Path(__file__).resolve().parents[1]/'infra/poi/data/places.json')
    parser.add_argument('--minimum-confidence',type=float,default=.7)
    args=parser.parse_args()
    try: bbox=tuple(float(x) for x in args.bbox.split(','))
    except ValueError: parser.error('bbox must contain four numeric coordinates')
    if len(bbox)!=4 or not (141.0<=bbox[0]<bbox[2]<=154.0 and -37.5<=bbox[1]<bbox[3]<=-28.0): parser.error('Use a bounded NSW area: west,south,east,north')
    if not 0<=args.minimum_confidence<=1: parser.error('confidence must be between 0 and 1')
    if args.input and not args.release: parser.error('--input requires the original --release version')
    if sys.platform=='darwin' and not os.getenv('SSL_CERT_FILE') and Path('/etc/ssl/cert.pem').exists(): os.environ['SSL_CERT_FILE']='/etc/ssl/cert.pem'
    with tempfile.TemporaryDirectory(prefix='buddy-overture-') as temporary:
        raw=args.input
        release=args.release
        if not raw:
            try:
                from overturemaps.core import get_latest_release
            except ImportError: parser.error('Install scripts/requirements-open-poi.txt in your Python venv first')
            release=release or get_latest_release()
            raw=Path(temporary)/'places.geojson'
            subprocess.run([sys.executable,'-m','overturemaps','download','--release',release,
                            '--bbox='+args.bbox,'--connect_timeout','30','--request_timeout','60',
                            '--type','place','-f','geojson','-o',str(raw)],check=True)
        if not re.fullmatch(r'\d{4}-\d{2}-\d{2}\.\d+',release): parser.error('Invalid Overture release version')
        index=normalize(json.loads(raw.read_text()),bbox,release,args.minimum_confidence)
        if not index['places']: raise SystemExit('No usable records; existing local snapshot was not changed.')
        args.output.parent.mkdir(parents=True,exist_ok=True)
        with tempfile.NamedTemporaryFile(mode='w',encoding='utf8',dir=args.output.parent,delete=False) as output:
            json.dump(index,output,ensure_ascii=False,separators=(',',':'));temp_name=output.name
        os.replace(temp_name,args.output)
        print(json.dumps({'release':release,'count':len(index['places']),
                          'categories':dict(Counter(p['searchCategory'] for p in index['places'])),
                          'output':str(args.output)},ensure_ascii=False))

if __name__=='__main__': main()
