#!/usr/bin/env python3
"""Import bounded public OSM map POIs for unified direct/Zetaris search.
A viewpoint/historic tag is an exploration candidate, not a popularity claim.
"""
import argparse,csv,hashlib,json,math,re,ssl,sys,time
from collections import Counter
from datetime import datetime,timezone
from pathlib import Path
from urllib.parse import urlencode
from urllib.request import Request,urlopen
ROOT=Path(__file__).resolve().parents[1]
BBOX=[151.18,-33.90,151.25,-33.82]
# Bounded NSW tourist areas (west,south,east,north). Whole-state export would be millions of rows, so we tile only places travellers visit.
REGIONS={
 'sydney-cbd-rocks':[151.17,-33.92,151.26,-33.84],'sydney-bondi':[151.25,-33.91,151.29,-33.87],'sydney-manly':[151.26,-33.81,151.31,-33.78],'parramatta':[151.00,-33.83,151.04,-33.79],
 'katoomba-leura':[150.28,-33.74,150.35,-33.70],'wentworth-falls':[150.35,-33.73,150.40,-33.69],'blackheath':[150.27,-33.66,150.31,-33.62],
 'hunter-pokolbin':[151.25,-32.80,151.35,-32.74],'newcastle':[151.72,-32.95,151.82,-32.90],'port-stephens-nelson-bay':[152.12,-32.74,152.20,-32.70],
 'terrigal-gosford':[151.40,-33.46,151.46,-33.42],'wollongong':[150.86,-34.44,150.92,-34.40],'kiama':[150.84,-34.69,150.87,-34.66],
 'jervis-bay-huskisson':[150.65,-35.05,150.75,-35.02],'bowral-mittagong':[150.40,-34.50,150.48,-34.46],'jindabyne-thredbo':[148.58,-36.43,148.64,-36.40],
 'byron-bay':[153.58,-28.66,153.64,-28.62],'coffs-harbour':[153.10,-30.32,153.14,-30.28],'port-macquarie':[152.89,-31.45,152.93,-31.42],
 'mudgee':[149.57,-32.61,149.61,-32.58],'orange':[149.08,-33.30,149.12,-33.27],'broken-hill':[141.44,-31.97,141.48,-31.94],
 'lord-howe-island':[159.04,-31.57,159.09,-31.52],'tamworth':[150.91,-31.11,150.95,-31.08],'albury':[146.90,-36.10,146.94,-36.06],'wagga-wagga':[147.35,-35.13,147.39,-35.10]}
def query(bbox):
    west,south,east,north=bbox;area=f'({south},{west},{north},{east})'
    filters=['[tourism~"^(viewpoint|hotel|hostel|motel|guest_house|attraction|museum|gallery)$"]','[historic]','[waterway=waterfall]','[shop]','[amenity~"^(restaurant|cafe|fast_food|food_court|pub|bar|nightclub|biergarten|ice_cream|toilets|drinking_water)$"]','[leisure~"^(park|garden|nature_reserve)$"][name]','[tourism~"^(camp_site|caravan_site)$"]']
    return '[out:json][timeout:120];('+''.join('nwr'+f+area+';' for f in filters)+');out center tags;'
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
        elif tourism in ('hotel','hostel','motel','guest_house','camp_site','caravan_site'):category='lodging';poi_kind=tourism
        elif amenity in ('restaurant','cafe','fast_food','food_court','pub','bar','nightclub','biergarten','ice_cream'):category='food';poi_kind=amenity
        elif tags.get('shop') and tags['shop'] not in ('no','vacant'):category='shops';poi_kind=tags['shop']
        elif tourism=='viewpoint' or historic or tags.get('waterway')=='waterfall':category='gems';poi_kind='waterfall' if tags.get('waterway')=='waterfall' else tourism or historic
        elif tourism in ('attraction','museum','gallery'):category='sights';poi_kind=tourism
        elif tags.get('leisure') in ('park','garden','nature_reserve'):category='sights';poi_kind=tags['leisure']
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
ENDPOINTS=['https://overpass-api.de/api/interpreter','https://overpass.kumi.systems/api/interpreter']
def fetch(bbox,tries=5,cache=None):
    key=cache/(('%.4f_%.4f_%.4f_%.4f'%tuple(bbox))+'-'+hashlib.sha256(query(bbox).encode()).hexdigest()[:12]+'.json') if cache else None
    if key and key.exists():return json.loads(key.read_text())
    raw=fetch_remote(bbox,tries)
    if key:cache.mkdir(parents=True,exist_ok=True);key.write_text(json.dumps(raw))
    return raw
def fetch_remote(bbox,tries=5):
    body=urlencode({'data':query(bbox)}).encode();last=None
    for attempt in range(tries):
        url=ENDPOINTS[attempt%len(ENDPOINTS)]
        try:
            request=Request(url,data=body,headers={'User-Agent':'SydneyBuddy/1.0 (https://github.com/f1sh11/hackathon)','Accept':'application/json'})
            with urlopen(request,timeout=120,context=ssl.create_default_context(cafile='/etc/ssl/cert.pem')) as response:raw=json.load(response)
            if raw.get('remark'):raise ValueError(raw['remark'])
            return raw
        except Exception as error:
            last=error;print(f'  retry {attempt+1}/{tries} {url}: {error}',file=sys.stderr);time.sleep(15*(attempt+1))
    raise RuntimeError(f'Overpass failed for {bbox}: {last}')
def tiles(bbox,step=0.1):
    west,south,east,north=bbox;nx=max(1,math.ceil((east-west)/step));ny=max(1,math.ceil((north-south)/step));dx=(east-west)/nx;dy=(north-south)/ny
    return [[west+i*dx,south+j*dy,west+(i+1)*dx,south+(j+1)*dy] for i in range(nx) for j in range(ny)]
def merge(parts,now):
    places={};boxes=[]
    for bbox,data in parts:
        boxes.append(bbox)
        for place in data['places']:places[place['id']]=place
    union=[min(b[0] for b in boxes),min(b[1] for b in boxes),max(b[2] for b in boxes),max(b[3] for b in boxes)]
    return {'schemaVersion':1,'source':'OpenStreetMap','bbox':union,'bboxes':boxes,'generatedAt':now,'places':list(places.values())}
def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--input',type=Path);parser.add_argument('--output',type=Path,default=ROOT/'infra/zetaris/data')
    parser.add_argument('--refresh',action='store_true',help='Fetch fresh responses instead of cached tiles');parser.add_argument('--region',action='append',choices=sorted(REGIONS),help='NSW tourist area; repeatable');parser.add_argument('--all-nsw',action='store_true',help='every bounded NSW area in REGIONS');args=parser.parse_args()
    now=datetime.now(timezone.utc).isoformat()
    if args.input:raw=json.loads(args.input.read_text());data=normalize(raw,BBOX,now)
    else:
        names=sorted(REGIONS) if args.all_nsw else args.region or []
        boxes=[(n,REGIONS[n]) for n in names] or [('sydney-cbd',BBOX)];parts=[];failed=[]
        for name,bbox in boxes:
            for tile in tiles(bbox):
                print(f'Fetching {name} {[round(v,3) for v in tile]}',file=sys.stderr)
                try:part=normalize(fetch(tile,cache=None if args.refresh else Path('/tmp/osm/tiles')),tile,now)
                except Exception as error:print(f'  SKIPPED {name}: {error}',file=sys.stderr);failed.append(name);continue
                print(f'  {name}: {len(part["places"])} places',file=sys.stderr);parts.append((tile,part));time.sleep(3)
        if failed:raise SystemExit('Incomplete region export; existing files unchanged: '+', '.join(sorted(set(failed))))
        if not parts:raise SystemExit('No tile succeeded; refuse empty export')
        data=merge(parts,now)
    write(data,args.output)
    print(json.dumps({'places':len(data['places']),'categories':dict(Counter(p['searchCategory'] for p in data['places']))},ensure_ascii=False))
if __name__=='__main__':main()
