"""Google Weather daily forecasts for Sydney. Never substitute sunny data on failure."""
import datetime as dt
from urllib.parse import urlencode

SOURCE='Google Maps · Weather API'

def get_weather(p,fetcher,key):
    dates=[(dt.date.fromisoformat(p['date'])+dt.timedelta(days=i)).isoformat() for i in range(p['days'])]
    value={'source':SOURCE,'rainy':False,'status':'unavailable','days':[],
           'fetchedAt':dt.datetime.now(dt.timezone.utc).isoformat()}
    try:
        if not key:raise ValueError('missing key')
        params=urlencode({'location.latitude':-33.8688,'location.longitude':151.2093,
                          'days':10,'pageSize':10,'languageCode':'en','unitsSystem':'METRIC'})
        raw=fetcher('https://weather.googleapis.com/v1/forecast/days:lookup?'+params,
                    headers={'X-Goog-Api-Key':key},timeout=8)
        records={}
        for row in raw['forecastDays']:
            d=row['displayDate'];records[dt.date(d['year'],d['month'],d['day']).isoformat()]=row
        for date in dates:
            row=records.get(date)
            if not row:
                value['days'].append({'date':date,'summary':'Outside the 10-day forecast range','rainy':False,'available':False});continue
            # Use the parts overlapping the travel hours. Daily temperatures cover the full day.
            parts=[]
            if p['startMinute']<1140 and p['endMinute']>420:parts.append(row.get('daytimeForecast',{}))
            if p['startMinute']<420 or p['endMinute']>1140:parts.append(row.get('nighttimeForecast',{}))
            probabilities=[x.get('precipitation',{}).get('probability',{}).get('percent') for x in parts]
            probabilities=[x for x in probabilities if isinstance(x,(int,float)) and 0<=x<=100]
            descriptions=list(dict.fromkeys(x.get('weatherCondition',{}).get('description',{}).get('text','') for x in parts))
            low=row.get('minTemperature',{}).get('degrees');high=row.get('maxTemperature',{}).get('degrees')
            if low is None or high is None or not probabilities:raise ValueError('incomplete forecast')
            probability=max(probabilities)
            rainy=probability>=60 or any('RAIN' in x.get('weatherCondition',{}).get('type','') or 'THUNDER' in x.get('weatherCondition',{}).get('type','') for x in parts)
            summary=f"{' / '.join(filter(None,descriptions))} · {low:g}–{high:g}°C · {probability:g}% chance of rain"
            value['days'].append({'date':date,'summary':summary,'rainy':rainy,'available':True,
                                  'temperatureMin':low,'temperatureMax':high,'precipitationProbability':probability})
        available=[d for d in value['days'] if d['available']]
        value['status']='ok' if len(available)==len(dates) else 'partial' if available else 'out_of_range'
        value['rainy']=any(d['rainy'] for d in available)
        value['summary']=' / '.join(f"{d['date'][5:]} {d['summary']}" for d in value['days'])
    except Exception:
        value.update(summary='Weather is unavailable; sunny conditions have not been assumed',status='unavailable',days=[{'date':d,'summary':'Weather is unavailable. Refresh later.','rainy':False,'available':False} for d in dates])
    return value
