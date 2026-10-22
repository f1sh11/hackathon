import unittest
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from planner import Planner,ServiceError,validate_input,parse_model_json,opening_window

BASE={'date':'2026-10-24','days':1,'budget':100,'maxSteps':10000,'startMinute':600,'endMinute':1080,'interests':['美食','风景']}
class PlanningTests(unittest.TestCase):
 def test_feasible_trip_accounts_for_return_and_internal_walking(self):
  p=Planner().plan(BASE);d=p['days'][0]
  self.assertLessEqual(d['estimatedSteps'],BASE['maxSteps']);self.assertLessEqual(d['estimatedCost'],100)
  self.assertEqual(len(d['legs']),len(d['stops'])+1)
  self.assertGreater(d['estimatedSteps'],sum(x['meters'] for x in d['legs'])/0.75)
  self.assertEqual(p['mode'],'demo')
 def test_budget_never_exceeded(self):
  p=Planner().plan(dict(BASE,budget=0));self.assertEqual(p['days'][0]['estimatedCost'],0)
 def test_infeasible_day_is_rejected(self):
  with self.assertRaises(ServiceError):Planner().plan(dict(BASE,startMinute=600,endMinute=610))
 def test_required_stop_is_not_silently_dropped(self):
  p=Planner().plan(dict(BASE,mustVisit=['opera']));self.assertIn('opera',[x['id'] for d in p['days'] for x in d['stops']])
 def test_infeasible_required_stop_budget(self):
  with self.assertRaises(ServiceError):Planner().plan(dict(BASE,budget=0,mustVisit=['mca']))
 def test_rain_scenario_is_not_real_weather(self):
  p=Planner().plan(dict(BASE,weatherOverride='rain'));self.assertIn('非实时',p['weather']['source'])
  self.assertTrue(all(s['indoor'] for s in p['days'][0]['stops']))
 def test_multiday_no_duplicate_attractions(self):
  p=Planner().plan(dict(BASE,days=2));ids=[x['id'] for d in p['days'] for x in d['stops']];self.assertEqual(len(ids),len(set(ids)))
 def test_bad_coordinate_rejected(self):
  with self.assertRaises(ServiceError):validate_input(dict(BASE,start={'lat':0,'lng':0}))
 def test_live_cannot_silently_fall_back(self):
  with self.assertRaises(ServiceError):Planner('live',{}).plan(BASE)
 def test_invalid_model_json_rejected(self):
  with self.assertRaises(ServiceError):parse_model_json('not json')
 def test_split_opening_hours_not_merged(self):
  point={'periods':[{'open':{'day':6,'hour':9},'close':{'day':6,'hour':12}},{'open':{'day':6,'hour':14},'close':{'day':6,'hour':17}}]}
  self.assertEqual(opening_window(point,'2026-10-24'),[(540,720),(840,1020)])
 def test_unknown_opening_hours_marked(self):
  p=Planner().plan(BASE);self.assertTrue(all(not s['hoursVerified'] for s in p['days'][0]['stops']))
if __name__=='__main__':unittest.main()
