#!/usr/bin/env python3
"""Prepare only public travel data for a separate Zetaris source database.
No user trips, preferences, secrets, or commercial-map responses are exported.
"""
import argparse
import csv
from datetime import date, datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import ssl
import tempfile
from urllib.request import Request, urlopen

ROOT=Path(__file__).resolve().parents[1]
FIELDS=['dataset','record_id','name','category','lat','lng','source','observed_at','payload']
WEATHER_URL='https://api.open-meteo.com/v1/forecast?latitude=-33.8688&longitude=151.2093&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=Australia%2FSydney&forecast_days=7'

def weather_records(raw, fetched_at):
    if raw.get('timezone')!='Australia/Sydney':raise ValueError('Weather must use Australia/Sydney timezone')
    daily=raw['daily'];keys=['weather_code','temperature_2m_max','temperature_2m_min','precipitation_probability_max']
    if any(len(daily[k])!=len(daily['time']) for k in keys):raise ValueError('Weather array length mismatch')
    records=[]
    for i,day in enumerate(daily['time']):
        date.fromisoformat(day)
        code,high,low,prob=(daily[k][i] for k in keys)
        valid=all(isinstance(v,(int,float)) and not isinstance(v,bool) and math.isfinite(v) for v in [code,high,low,prob])
        valid=valid and 0<=prob<=100 and low<=high
        records.append({'date':day,'available':bool(valid),'temperatureMin':low if valid else None,
                        'temperatureMax':high if valid else None,'precipitationProbability':prob if valid else None,
                        'weatherCode':code if valid else None,'rainy':bool(valid and (prob>=60 or code in {51,53,55,56,57,61,63,65,66,67,80,81,82,95,96,99})),
                        'source':'Open-Meteo','attribution':'Weather data by Open-Meteo · CC BY 4.0',
                        'fetchedAt':fetched_at,'timezone':'Australia/Sydney','scope':'daily, not hourly; Sydney city centre'})
    return records

def build_rows(snapshot,catalog,weather=None,now=None):
    now=now or datetime.now(timezone.utc).isoformat()
    if snapshot.get('schemaVersion')!=1 or snapshot.get('source')!='Overture Maps' or not snapshot.get('places'):raise ValueError('Import a real Overture snapshot first')
    if weather:raise ValueError("Weather is live through Zetaris REST; do not export forecast snapshots")
    rows=[];seen=set()
    def add(dataset,identifier,name,category,lat,lng,source,payload,observed=now):
        if (dataset,identifier) in seen:raise ValueError('Duplicate public record')
        if lat is not None and (not isinstance(lat,(int,float)) or not math.isfinite(lat) or not -90<=lat<=90):raise ValueError('Invalid latitude')
        if lng is not None and (not isinstance(lng,(int,float)) or not math.isfinite(lng) or not -180<=lng<=180):raise ValueError('Invalid longitude')
        seen.add((dataset,identifier));rows.append(dict(zip(FIELDS,[dataset,identifier,name,category,lat,lng,source,observed,json.dumps(payload,ensure_ascii=False,separators=(',',':'))])))
    for p in snapshot['places']:
        if not p['id'].startswith('overture:') or p.get('source')!='Overture Maps':raise ValueError('Only original open Overture POIs may be exported')
        add('poi',p['id'],p['name'],p['searchCategory'],p['lat'],p['lng'],'Overture Maps',p,snapshot['downloadedAt'])
    for p in catalog:add('catalog',p['id'],p['name'],p['category'],p['lat'],p['lng'],'SydneyBuddy demo catalog',p)
    for layer in json.loads((ROOT/'shared/travel-map-layers.json').read_text()):
        add('map_layers',layer['id'],layer['name'],'provider_metadata',None,None,'SydneyBuddy configuration',layer)
    add('metadata','overture','Overture snapshot','provenance',None,None,'Overture Maps',{k:v for k,v in snapshot.items() if k!='places'})
    return rows

def write_export(rows,output):
    output.mkdir(parents=True,exist_ok=True)
    with tempfile.NamedTemporaryFile(mode='w',encoding='utf8',newline='',dir=output,delete=False) as stream:
        writer=csv.DictWriter(stream,fieldnames=FIELDS);writer.writeheader();writer.writerows(rows);temporary=Path(stream.name)
    target=output/'travel-records.csv';os.replace(temporary,target)
    counts={kind:sum(r['dataset']==kind for r in rows) for kind in sorted({r['dataset'] for r in rows})}
    manifest={'schemaVersion':1,'generatedAt':datetime.now(timezone.utc).isoformat(),'counts':counts,'sha256':hashlib.sha256(target.read_bytes()).hexdigest(),'zetarisVerified':False}
    (output/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
    return manifest

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--poi',type=Path,default=ROOT/'infra/poi/data/places.json')
    parser.add_argument('--catalog',type=Path,default=ROOT/'server/places.json')
    parser.add_argument('--output',type=Path,default=ROOT/'infra/zetaris/data')
    args=parser.parse_args()
    result=write_export(build_rows(json.loads(args.poi.read_text()),json.loads(args.catalog.read_text())),args.output)
    print(json.dumps(result,ensure_ascii=False))
if __name__=='__main__':main()
