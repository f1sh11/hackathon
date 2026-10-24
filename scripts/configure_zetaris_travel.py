#!/usr/bin/env python3
"""Register/verify native live weather and public community views on local Zetaris.
Never caches the weather REST table; never accepts client SQL or remote Zetaris hosts.
"""
import argparse,json,sys,uuid
from pathlib import Path
from urllib.request import Request,urlopen
from urllib.error import HTTPError
ROOT=Path(__file__).resolve().parents[1]
WEATHER_URL='https://api.open-meteo.com/v1/forecast?latitude=-33.8688&longitude=151.2093&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=Australia%2FSydney&forecast_days=7'
WEATHER_SQL='SELECT to_json(struct(*)) AS payload FROM sydneybuddy_live.weather_sydney'
def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--platform-env',type=Path,required=True);parser.add_argument('--register',action='store_true');args=parser.parse_args()
    env={}
    for line in args.platform_env.read_text().splitlines():
        key,sep,value=line.partition('=')
        if sep and not key.startswith('#'):env[key.strip()]=value.strip().strip('\"\'')
    def post(url,body,headers={}):
        request=Request(url,data=json.dumps(body).encode(),headers={'Content-Type':'application/json',**headers})
        with urlopen(request,timeout=90) as response:return json.load(response)
    auth=post('http://127.0.0.1:3000/api/auth/login',{'username':env['ZETARIS_ADMIN_EMAIL'],'password':env['ZETARIS_ADMIN_PASSWORD']})
    def query(sql):
        return post('http://127.0.0.1:8888/api/v1.0/sql-editor/sqls/run',{'queryId':str(uuid.uuid4()),'sql':sql,'source':'SqlEditor','limit':20000},
                    {'Authorization':'Bearer '+auth['idToken'],'X-Org-ID':str(auth['orgId']),'X-Request-ID':str(uuid.uuid4())})
    if args.register:
        databases=query('SHOW LIGHTNING DATABASES')['data']
        if not any(row[0].lower()=='sydneybuddy_live' for row in databases):query('CREATE LIGHTNING DATABASE sydneybuddy_live DESCRIBE BY "SydneyBuddy live public APIs"')
        tables=query('SHOW TABLES FROM sydneybuddy_live')['data']
        if not any(row[0].lower()=='sydneybuddy_live.weather_sydney' for row in tables):
            query('CREATE LIGHTNING REST TABLE weather_sydney FROM sydneybuddy_live REQUEST(endpoint "'+WEATHER_URL+'",method "get",response_type "json",http_encoding "URLENCODED") HEADER () BODY ()')
        tables=query('SHOW TABLES FROM sydneybuddy_travel')['data']
        if not any(row[0].lower()=='sydneybuddy_travel.travel_hidden_gems' for row in tables):query('REGISTER DATASOURCE TABLE "travel_hidden_gems" FROM sydneybuddy_travel')
    if args.register:
        tables=query('SHOW TABLES FROM sydneybuddy_travel')['data']
        if not any(row[0].lower()=='sydneybuddy_travel.travel_osm_places' for row in tables):query('REGISTER DATASOURCE TABLE \"travel_osm_places\" FROM sydneybuddy_travel')
        query("UNCACHE TABLE sydneybuddy_live.weather_sydney")
    # Deliberately two uncached queries, preserving provider generation evidence.
    values=[]
    for _ in range(2):
        response=query(WEATHER_SQL)
        if response.get('total')!=1 or len(response['data'])!=1:raise ValueError('Incomplete weather query')
        raw=json.loads(response['data'][0][0]);assert raw['timezone']=='Australia/Sydney' and len(raw['daily']['time'])==7
        values.append({'timezone':raw['timezone'],'forecastDates':raw['daily']['time'],'providerGenerationMs':raw['generationtime_ms']})
    response=query('SELECT payload FROM sydneybuddy_travel.travel_hidden_gems')
    places=[json.loads(row[0]) for row in response['data']]
    expected=json.loads((ROOT/'infra/zetaris/data/hidden-gems.json').read_text())['places']
    if len(places)!=len(expected) or {p['id'] for p in places}!={p['id'] for p in expected}:raise ValueError('Community collection mismatch')
    osm_response=query('SELECT payload FROM sydneybuddy_travel.travel_osm_places')
    osm=[json.loads(row[0]) for row in osm_response['data']]
    osm_expected=json.loads((ROOT/'infra/zetaris/data/osm-map-places.json').read_text())['places']
    if {p['id'] for p in osm}!={p['id'] for p in osm_expected}:raise ValueError('OSM collection mismatch')
    print(json.dumps({'osmPlaces':len(osm),'realZetaris':True,'weatherMode':'native REST, no application snapshot/cache','weatherQueries':values,'hiddenGems':len(places),'photos':sum(len(p['photos']) for p in places),'reviews':sum(len(p['reviews']) for p in places)},ensure_ascii=False))
if __name__=='__main__':
    try:main()
    except Exception as e:
        # HTTP/SQL diagnostics can contain authentication details: only expose the error class.
        print(json.dumps({'status':'failed','errorType':type(e).__name__}),file=sys.stderr);sys.exit(1)
