"""Offline regression checks against public provider fixtures, not schematic paths."""
import datetime as dt, json, math, unittest
from pathlib import Path
from route_demo_connections import CACHE, SOURCE, distance

ROOT=Path(__file__).resolve().parents[1]

class DemoRoutesTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.trip=json.loads((ROOT/'ios/Resources/sydney-mixed-transport.json').read_text())
        cls.provider_shapes=[]
        for path in CACHE.glob('*.json'):
            value=json.loads(path.read_text())
            if not isinstance(value,dict) or 'response' not in value: continue
            response=value['response']
            if 'routes' in response:
                cls.provider_shapes += [[{'lat':p[1],'lng':p[0]} for p in r['geometry']['coordinates']] for r in response['routes']]
            elif 'legs' in response:
                cls.provider_shapes += [s['geometry'] for leg in response['legs'] for s in leg['steps']]

    def test_all_19_connections_have_real_geometry_or_bounded_boarding_access(self):
        count=0;boarding=0
        for day in self.trip['days']:
            plan=day['estimatedTransportPlan'];self.assertEqual(plan['source'],SOURCE)
            anchors=[day['start']]+day['stops']+[day['end']]
            self.assertEqual(len(plan['legs']),len(anchors)-1)
            for i,leg in enumerate(plan['legs']):
                with self.subTest(day=day['date'],leg=i):
                    count+=1;steps=leg['steps']
                    self.assertLessEqual(distance(anchors[i],steps[0]['geometry'][0]),3)
                    self.assertLessEqual(distance(anchors[i+1],steps[-1]['geometry'][-1]),3)
                    for left,right in zip(steps,steps[1:]):self.assertLessEqual(distance(left['geometry'][-1],right['geometry'][0]),3)
                    for step in steps:
                        shape=step['geometry']
                        if step['meters']==0:
                            self.assertLess(distance(shape[0],shape[-1]),0.01)
                        elif 'Estimated boarding access' in (step.get('agency') or ''):
                            boarding+=1;self.assertEqual(step['mode'],'walk');self.assertEqual(len(shape),2)
                            self.assertLessEqual(distance(shape[0],shape[-1]),20)
                        else:self.assertIn(shape,self.provider_shapes)
        self.assertEqual(count,19);self.assertGreater(boarding,0)

    def test_hotel_and_icebergs_access_are_at_the_actual_landmarks(self):
        hotel=self.trip['profile']['hotel']
        # Official hotel's address is 30 Pitt Street; original fixture was west of it.
        self.assertLess(distance(hotel,{'lat':-33.86277,'lng':151.20965}),5)
        icebergs=self.trip['days'][1]['stops'][-1]
        self.assertLess(distance(icebergs,{'lat':-33.8952732,'lng':151.2744078}),30)
        self.assertEqual(self.trip['days'][0]['end'],self.trip['days'][1]['start'])
        self.assertEqual(self.trip['days'][1]['end'],self.trip['days'][2]['start'])
        self.assertEqual(self.trip['days'][0]['start'],self.trip['days'][2]['end'])

    def test_schedule_is_continuous_and_uses_routed_travel_times(self):
        for day in self.trip['days']:
            for index,leg in enumerate(day['estimatedTransportPlan']['legs']):
                start=dt.datetime.fromisoformat(leg['departureTime']);end=dt.datetime.fromisoformat(leg['arrivalTime'])
                self.assertGreaterEqual(end,start)
                previous=start
                for step in leg['steps']:
                    begin=dt.datetime.fromisoformat(step['departureTime']);finish=dt.datetime.fromisoformat(step['arrivalTime'])
                    self.assertEqual(begin,previous);self.assertGreaterEqual(finish,begin);previous=finish
                    self.assertGreaterEqual((finish-begin).total_seconds(),step['seconds']+step.get('waitSeconds',0))
                self.assertLess((end-previous).total_seconds(),60)
                self.assertEqual(day['legs'][index]['minutes'],math.ceil((end-start).total_seconds()/60))
                if index<len(day['stops']):
                    stop=day['stops'][index]
                    self.assertEqual(stop['arrival'],end.strftime('%H:%M'))
                    next_start=dt.datetime.fromisoformat(day['estimatedTransportPlan']['legs'][index+1]['departureTime'])
                    self.assertEqual(next_start,end+dt.timedelta(minutes=stop['visitMinutes']))

    def test_full_trip_save_fits_the_existing_cloud_request_limit(self):
        payload=json.dumps({'trip':self.trip,'expectedRevision':0},ensure_ascii=False,separators=(',',':')).encode()
        self.assertLess(len(payload),230000)
        for day in self.trip['days']:
            self.assertTrue(all(not step['geometry'] for leg in day['legs'] for step in leg['steps']))

    def test_modes_and_cycle_return_are_complete(self):
        modes={step['mode'] for day in self.trip['days'] for leg in day['legs'] for step in leg['steps']}
        self.assertTrue({'rail','ferry','bus','walk','drive','cycle'}<=modes)
        loop=next(s for l in self.trip['days'][1]['estimatedTransportPlan']['legs'] for s in l['steps'] if s['mode']=='cycle')
        self.assertLess(distance(loop['geometry'][0],loop['geometry'][-1]),3)
        self.assertGreater(len(loop['geometry']),100)
        self.assertGreater(loop['meters'],3500);self.assertLess(loop['meters'],4500)
        self.assertTrue(all(-33.906<p['lat']<-33.893 and 151.227<p['lng']<151.242 for p in loop['geometry']), 'The loop must stay inside Centennial Park')

if __name__=='__main__':unittest.main()
