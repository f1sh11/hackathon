"""Build the bundled three-day Sydney preset; never books travel or calls AI."""
import datetime as dt,json
from pathlib import Path
import seed_sydney_mixed_transport as base
import route_demo_connections as routed

HOTEL='Sydney Harbour Marriott at Circular Quay'
URL='https://www.marriott.com/en-us/hotels/sydmc-sydney-harbour-marriott-hotel-at-circular-quay/overview/'
base.POINTS.update({
 'airport':('Sydney Airport · International Terminal',-33.9365,151.1663),
 'hotel':(HOTEL,-33.8627,151.2087),
 'drop':('Hotel luggage drop · subject to availability',-33.8627,151.2087),
 'checkin':('Hotel check-in & recharge',-33.8627,151.2087),
 'dinner':('Circular Quay dinner & evening stroll',-33.8614,151.2116),
 'checkout':('Hotel check-out & luggage hold',-33.8627,151.2087),
 'pickup':('Collect luggage at the hotel',-33.8627,151.2087),
 'manly':('Manly Wharf & Corso',-33.8008,151.2844),
 'shelly':('Shelly Beach',-33.8005,151.2968),
 'lunch':('Manly waterfront lunch',-33.8009,151.2848),
 'water1':('Harbour connection',-33.8524,151.2350),
 'water2':('Harbour connection',-33.8418,151.2582),
 'water3':('Harbour connection',-33.8210,151.2793),
 'green':('Airport train connection',-33.9230,151.1850),
})
points=routed.anchors(base)
first=dt.date(2026,10,28);base.FIRST=first
trip=base.make_trip(first)
trip['title']='Sydney, from arrival to goodbye'
d1=base.day(first,[
 ('drop',20,'Illustrative luggage drop only. Ask the hotel to confirm storage; early room access is not assumed.','住宿'),
 ('rocks',45,'Ease into Sydney with harbour lanes and a relaxed lunch break. Meals are not booked.','城市'),
 ('opera',50,'Enjoy the forecourt and harbour view; no performance ticket is included.','文化'),
 ('gardens',60,'A gentle garden wander, with time to sit down.','自然'),
 ('checkin',150,'Suggested check-in and rest from around 15:00. Room, check-in time and availability require confirmation; nothing is booked.','住宿'),
 ('dinner',75,'A relaxed dinner near the harbour followed by a short evening stroll. Choose a restaurant on the day.','美食'),
],[('transit',45,14000,['airport','green','central','townhall','wynyard','quay','drop']),('walk',10,650,['drop','rocks']),('walk',18,1200,['rocks','quay','opera']),('walk',20,1400,['opera','gardens']),('walk',25,1700,['gardens','quay','checkin']),('walk',10,600,['checkin','dinner']),('walk',10,600,['dinner','hotel'])],7200,start='airport',end='hotel',start_minute=600)
d2=base.day(first+dt.timedelta(days=1),[
 ('hire',25,'Arrange a bike and helmet; hire availability and prices are not checked.','自然'),
 ('picnic',75,'Return the bike at the same point after the Grand Drive loop, then enjoy a picnic break.','自然'),
 ('bondi',90,'Time for the beach and a late lunch without rushing.','自然'),
 ('icebergs',45,'A coastal stroll to the lookout. Pool entry and restaurant reservations are not included.','风景'),
],[('drive',30,7500,['hotel','oxford','paddington','hire']),('cycle',25,3800,['hire','picnic']),('drive',20,6200,['picnic','junction','bondiroad','bondi']),('walk',12,700,['bondi','icebergs']),('transit',55,12500,['icebergs','bondiroad','junction','oxford','museum','quay','hotel'])],4200,start='hotel',end='hotel')
d3=base.day(first+dt.timedelta(days=2),[
 ('checkout',25,'Illustrative check-out and luggage hold before the outing. Confirm storage and collection arrangements with the hotel.','住宿'),
 ('quay',10,'Allow time at the wharf; check the actual ferry departure and wharf before travelling.','城市'),
 ('manly',35,'Take in the harbour crossing, then wander through the Corso.','城市'),
 ('shelly',60,'A relaxed coastal walk and beach break. The return walk is included.','自然'),
 ('lunch',60,'Lunch near the wharf before heading back to the city. Meals are not booked.','美食'),
 ('pickup',20,'Collect luggage before the airport transfer; this is a planning assumption, not confirmed storage.','住宿'),
],[('walk',0,0,['hotel','checkout']),('walk',10,650,['checkout','quay']),('ferry',40,11500,['quay','water1','water2','water3','manly']),('walk',25,1800,['manly','shelly']),('walk',25,1800,['shelly','lunch']),('ferry',50,12200,['lunch','manly','water3','water2','water1','quay','pickup']),('transit',50,14000,['pickup','quay','wynyard','townhall','central','green','airport'])],6500,start='hotel',end='airport',start_minute=540)
for day in [d1,d2,d3]:
 for stop in day['stops']:stop['id']=stop['id'].replace('curated-sydney-','curated-three-day-')
trip['days']=[d1,d2,d3]
trip['profile'].update({'days':3,'start':base.point('airport'),'end':base.point('airport'),'hotel':base.point('hotel'),'accommodationStatus':'selected','startMinute':600,'bookings':'Illustrative two-night hotel stay only. No hotel, transfers, restaurant or flights have been booked.'})
trip['accommodationSuggestions']=[{'id':'curated-harbour-marriott','name':HOTEL,'nameEn':HOTEL,'category':'住宿','indoor':True,'visitMinutes':0,'cost':0,'lat':base.point('hotel')['lat'],'lng':base.point('hotel')['lng'],'reason':'Illustrative two-night base near Circular Quay. Not booked. Rates and availability need confirmation.','description':'Selected for this preset itinerary, not a confirmed reservation. Accommodation and transport are separate from the activity budget.','source':'Marriott official website · property information only; no live room search','website':URL,'address':'30 Pitt Street, Sydney NSW 2000','priceLabel':'Rate to confirm · 2 nights · not booked'}]
trip['explanation']='A relaxed three-day Sydney journey: airport arrival and harbour sights, a park cycling loop and Bondi, then a Manly ferry outing before returning to the airport. Sydney Harbour Marriott at Circular Quay is an illustrative two-night base, not a booking. All transport paths and times are preset estimates.'
trip['warnings'] += ['Hotel room rate, availability, luggage storage and check-in arrangements are unconfirmed. No bookings were made.','No flight is selected. Airport arrival is an estimate; check your flight and required airport buffer before using this itinerary.']
routed.apply(trip,base,points)
output=Path(__file__).resolve().parents[1]/'ios/Resources/sydney-mixed-transport.json'
output.write_text(json.dumps(trip,ensure_ascii=False,indent=2)+'\n')
print('Built three days; hotel overnight chain and final airport transfer included.')
