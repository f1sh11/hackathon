import unittest
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from weather import get_weather
from planner import Planner
P={'date':'2026-10-24','days':2,'startMinute':600,'endMinute':1080}
def row(day,prob):
 return {'displayDate':{'year':2026,'month':10,'day':day},'minTemperature':{'degrees':15},'maxTemperature':{'degrees':24},'daytimeForecast':{'weatherCondition':{'description':{'text':'多云'},'type':'CLOUDY'},'precipitation':{'probability':{'percent':prob}}}}
class WeatherTests(unittest.TestCase):
 def test_per_day_and_page_size(self):
  calls=[]
  def fetch(url,**kw):calls.append((url,kw));return {'forecastDays':[row(24,80),row(25,10)]}
  w=get_weather(P,fetch,'secret')
  self.assertEqual(w['status'],'ok');self.assertTrue(w['days'][0]['rainy']);self.assertFalse(w['days'][1]['rainy'])
  self.assertIn('pageSize=10',calls[0][0]);self.assertNotIn('secret',calls[0][0]);self.assertEqual(calls[0][1]['timeout'],8)
 def test_out_of_range(self):
  w=get_weather(dict(P,date='2027-01-01'),lambda *a,**k:{'forecastDays':[row(24,10)]},'key')
  self.assertEqual(w['status'],'out_of_range');self.assertFalse(w['days'][0]['available'])
 def test_failure_explicit(self):
  def fail(*a,**kw):raise TimeoutError()
  w=get_weather(P,fail,'key');self.assertEqual(w['status'],'unavailable');self.assertIn('未按晴天',w['summary'])
 def test_missing_probability_not_zero(self):
  r=row(24,0);del r['daytimeForecast']['precipitation']
  self.assertEqual(get_weather(P,lambda *a,**kw:{'forecastDays':[r]},'key')['status'],'unavailable')
 def test_daily_rain_does_not_filter_other_day(self):
  class TestPlanner(Planner):
   def weather(self,p):return {'summary':'mixed','rainy':True,'source':'test','days':[{'date':'2026-10-24','rainy':True},{'date':'2026-10-25','rainy':False}]}
  t=TestPlanner().plan(dict(P,budget=100,maxSteps=18000,interests=['风景']))
  self.assertTrue(all(x['indoor'] for x in t['days'][0]['stops']))
  self.assertTrue(any(not x['indoor'] for x in t['days'][1]['stops']))
