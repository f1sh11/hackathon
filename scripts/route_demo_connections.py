"""Cached provider routes for the three-day preset. No invented connecting lines.

Run build_three_day_demo.py --fetch-routes once to populate the public fixture cache.
Normal builds use the cache only. FOSSGIS requests are sequential, <= 1/second.
"""
import copy, datetime as dt, hashlib, json, math, subprocess, sys, time
from pathlib import Path

CACHE = Path(__file__).resolve().parent / 'fixtures' / 'sydney-routes'
FETCH = '--fetch-routes' in sys.argv
LAST = 0.0
SOURCE = 'Estimated schedule · cached OSM / TfNSW routes'
ANCHORS = {
 'airport':(-33.935691,151.164535),
 # Pitt Street public approach, beside the hotel's actual OSM building 335687066.
 # This is a street access point, not a claim of a surveyed interior doorway.
 'hotel':(-33.862776,151.209642),
 'rocks':(-33.8592,151.2085), 'opera':(-33.857017,151.215714),
 'gardens':(-33.8642,151.2166), 'dinner':(-33.8614,151.2116),
 'quay':(-33.861054,151.211032),
 'hire':(-33.9025905,151.2292587), 'bondi':(-33.8915,151.2767),
 'icebergs':(-33.89517,151.27433), 'manly':(-33.8008,151.2844),
 'shelly':(-33.8005,151.2968), 'lunch':(-33.8009,151.2848),
}
ALIASES = {'drop':'hotel','checkin':'hotel','checkout':'hotel','pickup':'hotel','picnic':'hire'}

def distance(a,b):
    p,q=map(math.radians,[a['lat'],b['lat']]); dp=q-p; dl=math.radians(b['lng']-a['lng'])
    return 12742000*math.asin(min(1,math.sqrt(math.sin(dp/2)**2+math.cos(p)*math.cos(q)*math.sin(dl/2)**2)))

def request(url, payload=None):
    global LAST
    key=hashlib.sha256((url+json.dumps(payload,sort_keys=True)).encode()).hexdigest()[:24]
    path=CACHE/(key+'.json')
    if path.exists(): return json.loads(path.read_text())['response']
    if not FETCH: raise RuntimeError('Missing route fixture. Rebuild with --fetch-routes: '+key)
    time.sleep(max(0,1.3-(time.monotonic()-LAST)))
    args=['curl','--fail-with-body','--silent','--show-error','--max-time','65','--retry','1','-A','SydneyBuddyRouteReview/1.0',url]
    if payload is not None: args+=['-H','Content-Type: application/json','--data-binary',json.dumps(payload)]
    result=subprocess.run(args,capture_output=True,text=True); LAST=time.monotonic()
    if result.returncode: raise RuntimeError('Route provider failed: '+result.stderr[:180]+' '+result.stdout[:150])
    data=json.loads(result.stdout)
    CACHE.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps({'url':url,'request':payload,'fetchedAt':dt.datetime.now(dt.timezone.utc).isoformat(),'response':data},ensure_ascii=False,indent=2)+'\n')
    print('Cached',key,flush=True)
    return data

def road(mode, points):
    profile={'walk':'foot','drive':'car','cycle':'bike'}[mode]
    coords=';'.join(f"{p['lng']:.6f},{p['lat']:.6f}" for p in points)
    url=f'https://routing.openstreetmap.de/routed-{profile}/route/v1/driving/{coords}?overview=full&geometries=geojson&steps=true'
    x=request(url)
    if x.get('code')!='Ok' or not x.get('routes'): raise RuntimeError('No road route: '+coords)
    r=x['routes'][0]
    geometry=[{'lat':p[1],'lng':p[0]} for p in r['geometry']['coordinates']]
    if len(geometry)<2: raise RuntimeError('Missing provider geometry')
    return {'mode':mode,'meters':r['distance'],'seconds':r['duration'],'geometry':geometry,
            'line':None,'headsign':None,'realtime':False,'stopCount':None,'waitSeconds':0,'platform':None,
            'agency':'FOSSGIS / OpenStreetMap · cached route','walkingDirections':None,
            'from':points[0]['name'],'to':points[-1]['name']}

def anchors(base):
    result={}
    for key,(lat,lng) in ANCHORS.items():
        x=request(f'https://routing.openstreetmap.de/routed-foot/nearest/v1/driving/{lng:.6f},{lat:.6f}?number=1')
        w=x['waypoints'][0]
        if w['distance']>65: raise RuntimeError('No nearby public access: '+key)
        result[key]={'name':base.POINTS[key][0],'lat':w['location'][1],'lng':w['location'][0]}
    for key,target in ALIASES.items(): result[key]={**result[target],'name':base.POINTS[key][0]}
    for key,p in result.items(): base.POINTS[key]=(p['name'],p['lat'],p['lng'])
    return result

def connector(a,b):
    if distance(a,b)<=3: return []
    step=road('walk',[a,b])
    if distance(step['geometry'][0],a)>3 or distance(step['geometry'][-1],b)>3:
        raise RuntimeError('Unrouted access gap: '+a['name']+' -> '+b['name'])
    return [step]

def connected_road(mode,a,b,via=None):
    step=road(mode,[a]+(via or [])+[b])
    entry={**step['geometry'][0],'name':a['name']+' · '+('pick-up point' if mode=='drive' else 'path access')}
    exit={**step['geometry'][-1],'name':b['name']+' · '+('drop-off point' if mode=='drive' else 'path access')}
    step['from']=entry['name'];step['to']=exit['name']
    return connector(a,entry)+[step]+connector(exit,b)

def transit(a,b,date,minute,required):
    depart=dt.datetime.fromisoformat(date+'T00:00:00+11:00')+dt.timedelta(minutes=minute)
    x=request('http://127.0.0.1:8787/journey/plan',{'points':[{**a,'stayMinutes':0},{**b,'stayMinutes':0}],
        'modes':['transit'],'departureTime':depart.isoformat(),'routingProvider':'nsw','mapProvider':'apple','contentSource':'open'})
    steps=x['legs'][0]['steps']
    if not any(s['mode']==required for s in steps): raise RuntimeError('Requested '+required+' not returned by TfNSW')
    for s in steps:
        if len(s['geometry'])<2: raise RuntimeError('TfNSW omitted route geometry')
        s['agency']=(s.get('agency') or 'Transport for NSW')+' · cached route'
        s['realtime']=False
    first={**steps[0]['geometry'][0],'name':steps[0]['from']}
    last={**steps[-1]['geometry'][-1],'name':steps[-1]['to']}
    joined=[]
    for step in steps:
        if joined:
            left=joined[-1]['geometry'][-1];right=step['geometry'][0];gap=distance(left,right)
            if gap>3:
                if gap>20 or (joined[-1]['mode']=='walk')==(step['mode']=='walk'):
                    raise RuntimeError('Unmapped station transfer: '+str(round(gap,1))+'m')
                # Vehicle centreline vs. pavement: a bounded, explicitly estimated
                # boarding connection, never a substitute for a missing street route.
                joined.append({'mode':'walk','from':joined[-1]['to'],'to':step['from'],
                    'meters':gap,'seconds':math.ceil(gap/1.2),'waitSeconds':0,'realtime':False,
                    'agency':'Estimated boarding access · vehicle to pavement · maximum 20m',
                    'geometry':[left,right]})
        step['meters']=max(step['meters'],sum(distance(x,y) for x,y in zip(step['geometry'],step['geometry'][1:])))
        joined.append(step)
    return connector(a,first)+joined+connector(last,b)

def apply(trip,base,points):
    sequences=[['airport','drop','rocks','opera','gardens','checkin','dinner','hotel'],
               ['hotel','hire','picnic','bondi','icebergs','hotel'],
               ['hotel','checkout','quay','manly','shelly','lunch','pickup','airport']]
    evidence=[]
    for di,(day,keys) in enumerate(zip(trip['days'],sequences)):
        cursor=day['planningStartMinute']*60
        for i,(ak,bk) in enumerate(zip(keys,keys[1:])):
            a,b=points[ak],points[bk]; leg=day['legs'][i]
            if distance(a,b)<1 and not (di==1 and i==1): steps=[]
            elif (di,i) in [(0,0),(1,4),(2,2),(2,5),(2,6)]:
                required='ferry' if (di,i) in [(2,2),(2,5)] else 'bus' if di==1 else 'rail'
                if di==1:
                    street={'name':'Museum Station · Elizabeth Street access','lat':-33.875542,'lng':151.209491}
                    steps=transit(a,street,day['date'],900,required)+connected_road('walk',street,b)
                else: steps=transit(a,b,day['date'],cursor//60,required)
            else:
                via=None
                if di==1 and i==1:
                    ring=json.loads((CACHE/'grand-drive-way.json').read_text())
                    via=[{**p,'name':'Grand Drive cycle lane'} for p in ring['via']]
                steps=connected_road(leg['mode'],a,b,via)
            # Only provider geometry; validate every handover including platforms.
            if not steps:
                steps=[{'mode':'walk','from':a['name'],'to':b['name'],'meters':0,'seconds':0,'waitSeconds':0,
                    'geometry':[{'lat':a['lat'],'lng':a['lng']}]*2,'realtime':False,'agency':'Same location · no transfer'}]
            if distance(a,steps[0]['geometry'][0])>3 or distance(b,steps[-1]['geometry'][-1])>3:
                raise RuntimeError(f'Endpoint gap on day {di+1} leg {i+1}')
            for left,right in zip(steps,steps[1:]):
                if distance(left['geometry'][-1],right['geometry'][0])>3:
                    raise RuntimeError(f'Provider handover gap on day {di+1} leg {i+1}: '+str(distance(left['geometry'][-1],right['geometry'][0])))
            start=cursor
            for s in steps:
                # A cached departure is illustrative when the user's date changes.
                duration=max(0,math.ceil(s['seconds']+s.get('waitSeconds',0)))
                s['departureTime']=base.stamp(dt.date.fromisoformat(day['date']),0)
                midnight=dt.datetime.fromisoformat(s['departureTime'])
                s['departureTime']=(midnight+dt.timedelta(seconds=cursor)).isoformat()
                cursor+=duration
                s['arrivalTime']=(midnight+dt.timedelta(seconds=cursor)).isoformat()
            # Round up the schedule at each stop; keep the actual geometry's travel time.
            cursor=math.ceil(cursor/60)*60
            departure=(midnight+dt.timedelta(seconds=start)).isoformat()
            arrival=(midnight+dt.timedelta(seconds=cursor)).isoformat()
            meters=sum(s['meters'] for s in steps)
            leg.update({'meters':round(meters),'minutes':(cursor-start)//60,'walkingMeters':round(sum(s['meters'] for s in steps if s['mode']=='walk')),
                        'source':SOURCE,'departureTime':departure,'arrivalTime':arrival,'steps':[{**s,'geometry':[]} for s in steps],'polyline':''})
            j=day['estimatedTransportPlan']['legs'][i]
            j.update({'from':a['name'],'to':b['name'],'departureTime':departure,'arrivalTime':arrival,'steps':steps})
            if i<len(day['stops']):
                stop=day['stops'][i];stop.update({'lat':b['lat'],'lng':b['lng'],'arrival':base.clock(cursor//60),'mapsURL':f'https://maps.apple.com/?ll={b["lat"]},{b["lng"]}'})
                # Do not imply early hotel room access.
                if bk=='checkin' and cursor<15*3600:
                    stop['visitMinutes']+=math.ceil((15*3600-cursor)/60)
                cursor+=stop['visitMinutes']*60
                stop['departure']=base.clock(cursor//60);j['staySeconds']=stop['visitMinutes']*60
            evidence.append({'day':di+1,'leg':i+1,'from':a['name'],'to':b['name'],'modes':[s['mode'] for s in steps],
                             'vertices':sum(len(s['geometry']) for s in steps),'meters':round(meters),
                             'startGapMeters':round(distance(a,steps[0]['geometry'][0]),2),'endGapMeters':round(distance(b,steps[-1]['geometry'][-1]),2)})
        day['start']=points[keys[0]];day['end']=points[keys[-1]];day['finishTime']=base.clock(cursor//60)
        day['estimatedSteps']=math.ceil(sum(l['walkingMeters'] for l in day['legs'])/0.75)
        plan=day['estimatedTransportPlan']
        plan.update({'source':SOURCE,'arrivalTime':arrival,'travelSeconds':sum(l['minutes']*60 for l in day['legs']),
                     'elapsedSeconds':cursor-day['planningStartMinute']*60,'meters':sum(l['meters'] for l in day['legs']),
                     'warnings':['Cached road and TfNSW route shapes. Times are illustrative, not live departures. Confirm services and access before travelling.']})
        day['warnings']=plan['warnings']+['Hotel stay, luggage storage and bike hire are unconfirmed. Nothing is booked.']
    trip['profile'].update({'start':points['airport'],'end':points['airport'],'hotel':points['hotel']})
    for hotel in trip['accommodationSuggestions']: hotel.update({'lat':points['hotel']['lat'],'lng':points['hotel']['lng']})
    trip['explanation']='Three days in Sydney with cached, provider-routed connections: airport train, harbour walks, a park cycle loop, Bondi by car and bus, and the Manly ferry. Schedules are illustrative; the hotel is suggested and not booked.'
    trip['routeGeometryVersion']=2
    trip['trace'].append({'title':'Provider-routed Demo connections','detail':'Every road segment is cached from FOSSGIS / OSM; rail, bus and ferry shapes plus station access are from TfNSW. Road endpoints and handovers must be within 3 metres. Vehicle-to-pavement boarding links are explicitly estimated and limited to 20 metres. Hotel marker is the mapped Pitt Street public approach, not an interior doorway.','source':'FOSSGIS / OpenStreetMap; Transport for NSW / Trip Planner','fetchedAt':dt.datetime.now(dt.timezone.utc).isoformat()})
    (CACHE/'validation.json').write_text(json.dumps(evidence,indent=2)+'\n')
    print('Validated',len(evidence),'connections. Maximum endpoint/handover gap: 3m.',flush=True)
