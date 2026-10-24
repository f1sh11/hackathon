#!/usr/bin/env python3
"""Import bounded public OSM map POIs for unified direct/Zetaris search.
A viewpoint/historic tag is an exploration candidate, not a popularity claim.
"""
import argparse,csv,json,math,re,ssl
from collections import Counter
from datetime import datetime,timezone
from pathlib import Path
from urllib.parse import urlencode
from urllib.request import Request,urlopen
ROOT=Path(__file__).resolve().parents[1]
BBOX=[151.18,-33.90,151.25,-33.82]
def query(bbox):
    west,south,east,north=bbox;area=f'({south},{west},{north},{east})'
    filters=['[tourism~"^(viewpoint|hotel|hostel|motel|guest_house|attraction|museum|gallery)$"]','[historic]','[waterway=waterfall]','[shop]','[amenity~"^(restaurant|cafe|fast_food|food_court|toilets|drinking_water)$"]']
    return '[out:json][timeout:40];('+''.join('nwr'+f+area+';' for f in filters)+');out center tags;'
def normalize(raw,bbox,now):
    if raw.get('remark'):raise ValueError('Incomplete Overpass response')
    result={}
    for element in raw.get('elements',[]):
        tags=element.get('tags',{});name=tags.get('name:zh',tags.get('name','')).strip();point=element.get('center',element)
        lat=point.get('lat');lng=point.get('lon');kind=element.get('type');identifier=element.get('id')
        if not name or len(name)>200 or kind not in ('node','way','relation') or not isinstance(identifier,int):continue
        if tags.get('access') in ('private','no') or tags.get('tourism') in ('apartment',):continue
        if not all(isinstance(v,(int,float)) and math.isfinite(v) for v in [lat,lng]):continue
        if not bbox[0]<=lng<=bbox[2] or not bbox[1]<=lat<=bbox[3]:continue
        amenity=tags.get('amenity','');tourism=tags.get('tourism','');historic=tags.get('historic','')
        if amenity in ('toilets','drinking_water'):category='facilities';poi_kind=amenity
        elif tourism in ('hotel','hostel','motel','guest_house'):category='lodging';poi_kind=tourism
        elif amenity in ('restaurant','cafe','fast_food','food_court'):category='food';poi_kind=amenity
        elif tags.get('shop') and tags['shop'] not in ('no','vacant'):category='shops';poi_kind=tags['shop']
        elif tourism=='viewpoint' or historic or tags.get('waterway')=='waterfall':category='gems';poi_kind='waterfall' if tags.get('waterway')=='waterfall' else tourism or historic
        elif tourism in ('attraction','museum','gallery'):category='sights';poi_kind=tourism
        else:continue
        address=', '.join(filter(None,[tags.get('addr:housenumber'),tags.get('addr:street'),tags.get('addr:suburb'),tags.get('addr:postcode')]))
        source_url=f'https://www.openstreetmap.org/{kind}/{identifier}'
        text=tags.get('description:zh',tags.get('description','')) or f'OSM {poi_kind.replace("_"," ")}'
        record={'id':f'osm:{kind}:{identifier}','name':name,'lat':lat,'lng':lng,'source':'OpenStreetMap','sourceUrl':source_url,
                'sourceLicense':'ODbL 1.0','retrievedAt':now,'searchCategory':category,'searchText':' '.join([name,tags.get('name',''),address,poi_kind]),
                'category':{'food':'美食','lodging':'住宿','shops':'店家','facilities':'設施','gems':'風景','sights':'風景'}[category],
                'poiKind':poi_kind,'description':text,'address':address,'website':tags.get('website',tags.get('contact:website','')),
                'phone':tags.get('phone',tags.get('contact:phone','')),'openingHoursText':tags.get('opening_hours',''),
                'wikidata':tags.get('wikidata',''),'commonsFile':tags.get('wikimedia_commons',''),
                'hiddenGem':category=='gems','accessStatus':'unverified','indoor':tags.get('indoor')=='yes',
                'visitMinutes':{'food':45,'lodging':30,'shops':30,'facilities':10}.get(category,60),'cost':0,
                'reason':'由 OSM 公開地點加入；非熱門度評分','note':'營業狀態、票價、房價與排隊未知；住宿地點加入工具箱不代表已訂房。'}
        result[record['id']]=record
    return {'schemaVersion':1,'source':'OpenStreetMap','bbox':bbox,'generatedAt':now,'places':list(result.values())}
def write(data,output):
    if not data['places']:raise ValueError('No public POIs; refuse empty export')
    output.mkdir(parents=True,exist_ok=True)
    (output/'osm-map-places.json').write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n')
    with (output/'osm-map-places.csv').open('w',newline='') as f:
        w=csv.writer(f);w.writerow(['id','name','lat','lng','payload'])
        for p in data['places']:w.writerow([p['id'],p['name'],p['lat'],p['lng'],json.dumps(p,ensure_ascii=False,separators=(',',':'))])
def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--input',type=Path);parser.add_argument('--output',type=Path,default=ROOT/'infra/zetaris/data');args=parser.parse_args()
    if args.input:raw=json.loads(args.input.read_text())
    else:
        request=Request('https://overpass-api.de/api/interpreter',data=urlencode({'data':query(BBOX)}).encode(),headers={'User-Agent':'SydneyBuddy/1.0 (https://github.com/f1sh11/hackathon)'})
        with urlopen(request,timeout=55,context=ssl.create_default_context(cafile='/etc/ssl/cert.pem')) as response:raw=json.load(response)
    data=normalize(raw,BBOX,datetime.now(timezone.utc).isoformat());write(data,args.output)
    print(json.dumps({'places':len(data['places']),'categories':dict(Counter(p['searchCategory'] for p in data['places']))},ensure_ascii=False))
if __name__=='__main__':main()
