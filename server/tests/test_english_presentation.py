"""English app copy must not change user facts or legacy preference identifiers."""
import json
from pathlib import Path
import re
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'server'))
from planner import CATALOG, Planner
from weather import get_weather

HAN = re.compile(r'[\u3400-\u9fff]')


class EnglishPresentationTests(unittest.TestCase):
    def assert_english(self, value):
        self.assertIsNone(HAN.search(value), value)

    def test_catalog_and_ticket_display_fields_are_english(self):
        for place in CATALOG:
            for field in ('name', 'note', 'description', 'locationSource'):
                if field in place:
                    self.assert_english(place[field])
        offers = json.loads((ROOT / 'shared/activity-offers.json').read_text())
        for offer in offers['offers']:
            for field in ('title', 'venueName', 'ticketType', 'conditions', 'sourceName'):
                self.assert_english(offer[field])
        # Existing API preference identifiers still match the catalog.
        self.assertTrue(any(place['category'] == '艺术' for place in CATALOG))

    def test_demo_plan_returns_english_copy_and_preserves_user_location(self):
        plan = Planner().plan({
            'date': '2026-10-26', 'days': 1, 'budget': 100, 'maxSteps': 10000,
            'startMinute': 600, 'endMinute': 1080, 'interests': ['艺术'],
            'start': {'name': '我的酒店', 'lat': -33.8611, 'lng': 151.2126},
        })
        self.assertEqual(plan['profile']['start']['name'], '我的酒店')
        for text in [plan['title'], plan['explanation'], plan['weather']['summary'],
                     *plan['warnings'], *plan['days'][0]['warnings']]:
            self.assert_english(text)
        for step in plan['trace']:
            for field in ('title', 'detail', 'source'):
                self.assert_english(step[field])

    def test_weather_provider_is_requested_in_english(self):
        calls = []

        def fetch(url, **kwargs):
            calls.append(url)
            return {'forecastDays': []}

        weather = get_weather({'date': '2026-10-26', 'days': 1,
                               'startMinute': 600, 'endMinute': 1080}, fetch, 'test-key')
        self.assertIn('languageCode=en', calls[0])
        self.assert_english(weather['summary'])
        self.assertEqual(weather['status'], 'out_of_range')


if __name__ == '__main__':
    unittest.main()
