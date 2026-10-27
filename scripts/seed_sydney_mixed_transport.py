#!/usr/bin/env python3
"""Create a new curated Sydney trip. Estimated transport is never live evidence.
No credentials, existing-trip edits, bookings, or preference writes.
Run: python3 scripts/seed_sydney_mixed_transport.py --date 2026-10-28
"""
import argparse, datetime as dt, json, urllib.request
from pathlib import Path
from zoneinfo import ZoneInfo

TITLE = 'Harbour, parks & coast'
SOURCE = 'Estimated connections · curated, not live'
POINTS = {
 'central': ('Central Station', -33.8830,151.2065),
 'townhall': ('Town Hall',-33.8734,151.2069),
 'wynyard': ('Wynyard',-33.8657,151.2058),
 'quay': ('Circular Quay',-33.8610,151.2111),
 'museum': ('Museum',-33.8756,151.2095),
 'oxford': ('Oxford Street',-33.8817,151.2192),
 'paddington': ('Paddington',-33.8866,151.2290),
 'junction': ('Bondi Junction',-33.8913,151.2473),
 'bondiroad': ('Bondi Road',-33.8940,151.2631),
 'rocks': ('The Rocks',-33.8592,151.2085),
 'opera': ('Sydney Opera House forecourt',-33.8571,151.2153),
 'gardens': ('Royal Botanic Garden',-33.8642,151.2166),
 'gallery': ('Art Gallery of NSW',-33.8688,151.2173),
 'hire': ('Centennial Park cycle hire',-33.9042,151.2350),
 'picnic': ('Centennial Park picnic',-33.9042,151.2350),
 'bondi': ('Bondi Beach',-33.8915,151.2767),
 'icebergs': ('Bondi Icebergs lookout',-33.8918,151.2792),
}
def point(key):
 n,lat,lng=POINTS[key];return {'name':n,'lat':lat,'lng':lng}
def geometry(keys):return [{'lat':point(k)['lat'],'lng':point(k)['lng']} for k in keys]
def stamp(date,minute):
 return dt.datetime.combine(date,dt.time(minute//60,minute%60),ZoneInfo('Australia/Sydney')).isoformat()
def clock(minute):return f'{minute//60:02d}:{minute%60:02d}'
def day(date,stops,connections,steps,start='central',end='central',start_minute=570):
 origin_key=start;end_key=end
 cursor=start_minute;visits=[];legs=[];journeys=[]
 keys=[start]+[s[0] for s in stops]+[end]
 for i,(mode,minutes,meters,via) in enumerate(connections):
  ferry=mode=='ferry';mode='transit' if ferry else mode
  a,b=keys[i:i+2];start=stamp(date,cursor);cursor+=minutes;end=stamp(date,cursor)
  coords=geometry(via or [a,b])
  # Cycling is a loop from the rental point and back, not a one-way hire.
  if mode=='cycle':
   coords=[{'lat':lat,'lng':lng} for lat,lng in [(-33.9042,151.2350),(-33.9015,151.2320),(-33.8974,151.2317),(-33.8951,151.2344),(-33.8955,151.2393),(-33.8986,151.2425),(-33.9030,151.2420),(-33.9061,151.2394),(-33.9042,151.2350)]]
  step_mode='ferry' if ferry else 'rail' if mode=='transit' and i==0 and date==FIRST else 'bus' if mode=='transit' else mode
  step={'mode':step_mode,'line':None,'headsign':None,'from':point(a)['name'],'to':point(b)['name'],'departureTime':start,'arrivalTime':end,'realtime':False,'stopCount':None,'meters':meters,'seconds':minutes*60,'waitSeconds':0,'platform':None,'agency':None,'geometry':coords}
  journey_steps=[step]
  if mode=='transit' and i==0 and date==FIRST and a=='central':
   change=stamp(date,570+19)
   rail={**step,'to':point('quay')['name'],'arrivalTime':change,'seconds':19*60,'meters':4400,'geometry':geometry(['central','townhall','wynyard','quay'])}
   walk={**step,'mode':'walk','from':point('quay')['name'],'departureTime':change,'seconds':6*60,'meters':400,'geometry':geometry(['quay','rocks'])}
   journey_steps=[rail,walk]
  legs.append({'mode':mode,'meters':meters,'minutes':minutes,'walkingMeters':meters if mode=='walk' else 0,'polyline':'','source':SOURCE,'steps':journey_steps,'departureTime':start,'arrivalTime':end})
  stay=stops[i][1] if i<len(stops) else 0
  journeys.append({'index':i,'mode':mode,'from':point(a)['name'],'to':point(b)['name'],'departureTime':start,'arrivalTime':end,'staySeconds':stay*60,'steps':journey_steps})
  if i<len(stops):
   key,duration,note,category=stops[i];p=point(key)
   visits.append({**p,'id':'curated-sydney-'+key,'category':category,'indoor':key=='gallery','visitMinutes':duration,'cost':0,'costKnown':False,'note':note,'arrival':clock(cursor),'departure':clock(cursor+duration),'hoursVerified':False,'mapsURL':f'https://maps.apple.com/?ll={p["lat"]},{p["lng"]}','steps':600})
   cursor+=duration
 plan={'departureTime':stamp(date,start_minute),'arrivalTime':stamp(date,cursor),'travelSeconds':sum(c[1] for c in connections)*60,'elapsedSeconds':(cursor-start_minute)*60,'meters':sum(c[2] for c in connections),'legs':journeys,'warnings':['Travel times and connections are estimates. Paths are illustrative; check live services, access and bike availability before travelling.'],'source':SOURCE}
 return {'date':date.isoformat(),'stops':visits,'legs':legs,'estimatedCost':0,'estimatedSteps':steps,'finishTime':clock(cursor),'warnings':plan['warnings'],'start':point(origin_key),'end':point(end_key),'planningStartMinute':start_minute,'estimatedTransportPlan':plan}

def make_trip(first):
 global FIRST;FIRST=first
 days=[day(first,[('rocks',45,'A relaxed start among the harbour lanes.','城市'),('opera',50,'Enjoy the forecourt and harbour views; no performance ticket included.','文化'),('gardens',75,'A gentle garden wander with time to pause.','自然'),('gallery',75,'Choose an exhibition after checking current opening hours.','艺术')],[('transit',25,4800,['central','townhall','wynyard','quay','rocks']),('walk',18,1200,['rocks','quay','opera']),('walk',20,1400,['opera','gardens']),('walk',12,800,['gardens','gallery']),('transit',30,3900,['gallery','museum','central'])],6500),
 day(first+dt.timedelta(days=1),[('hire',25,'Arrange a rental bike and helmet. Availability and price need checking.','自然'),('picnic',60,'Return the bike to the same rental point after the Grand Drive loop, then take a picnic break.','自然'),('bondi',75,'Beach time with room for a late lunch; meals are separate.','自然'),('icebergs',40,'An easy coastal walk to the lookout; no pool admission included.','风景')],[('drive',25,6500,['central','oxford','paddington','hire']),('cycle',25,3800,['hire','picnic']),('drive',20,6200,['picnic','junction','bondiroad','bondi']),('walk',12,700,['bondi','icebergs']),('transit',50,10000,['icebergs','bondiroad','junction','oxford','museum','central'])],3800)]
 return {'title':TITLE,'mode':'assisted','profile':{'date':first.isoformat(),'days':2,'budget':100,'maxSteps':10000,'startMinute':570,'endMinute':1080,'interests':['Nature','Art','Coast'],'mustVisit':[],'start':point('central'),'end':point('central'),'transportPreference':'balanced','travelers':1,'budgetCurrency':'AUD','budgetBasis':'perPersonPerDay','request':'A curated harbour, park and coast trip with estimated mixed transport.'},'days':days,'weather':{'summary':'Check the forecast before heading out','rainy':False,'source':'Not checked','status':'unavailable'},'explanation':'Harbour views on foot, a park cycling loop, and a relaxed coast afternoon. Public transport and taxi transfers keep longer connections off your feet. Transport times and paths are illustrative estimates, not live routing.','warnings':['Transport fares, bike hire, meals and admission prices are unverified and excluded from the activity estimate.','Driving legs assume taxi or rideshare, not a booked vehicle.','Cycling assumes returning a hired bike to the same location. Check availability, access and local conditions.'],'trace':[{'title':'Curated place selection','detail':'Real Sydney destinations with estimated connections. No live transport or feasibility verification claimed.','source':'Curated itinerary','fetchedAt':dt.datetime.now(dt.timezone.utc).isoformat()}],'createdAt':dt.datetime.now(dt.timezone.utc).isoformat()}

if __name__=='__main__':
 parser=argparse.ArgumentParser();parser.add_argument('--date',required=True);parser.add_argument('--backend',default='http://127.0.0.1:8787');parser.add_argument('--output');parser.add_argument('--save',action='store_true');args=parser.parse_args()
 trip=make_trip(dt.date.fromisoformat(args.date))
 if args.output:Path(args.output).write_text(json.dumps(trip,ensure_ascii=False,indent=2)+'\n')
 if args.save:
  existing=json.load(urllib.request.urlopen(args.backend+'/trips',timeout=10))
  if any(t.get('title')==TITLE for t in existing):raise SystemExit('A curated trip already exists; no existing trip was changed.')
  request=urllib.request.Request(args.backend+'/trips',data=json.dumps({'trip':trip}).encode(),headers={'Content-Type':'application/json'},method='POST')
  saved=json.load(urllib.request.urlopen(request,timeout=15));print('Created curated trip:',saved.get('id'))
 elif not args.output:print(json.dumps(trip,ensure_ascii=False,indent=2))
