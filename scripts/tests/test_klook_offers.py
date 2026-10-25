"""Synthetic fixtures, not live Klook prices."""
import importlib.util
import json
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('klook', Path(__file__).parents[1] / 'import_klook_offers.py')
klook = importlib.util.module_from_spec(spec)
spec.loader.exec_module(klook)
URL = 'https://www.klook.com/en-AU/activity/123-test-tour/'


class KlookTests(unittest.TestCase):
    def page(self, obj):
        return '<script type="application/ld+json">' + json.dumps(obj) + '</script>'

    def test_product_with_explicit_currency(self):
        result = klook.parse_page(self.page({'@type': 'Product', 'name': 'Synthetic tour',
            'offers': {'@type': 'AggregateOffer', 'lowPrice': '42.50', 'priceCurrency': 'AUD'},
            'aggregateRating': {'ratingValue': '4.5', 'reviewCount': '12'}}), URL)
        row = result['offers'][0]
        self.assertEqual(row['prices'][0]['amount'], 42.5)
        self.assertFalse(row['prices'][0]['budgetEligible'])
        self.assertEqual(row['reviewCount'], 12)
        self.assertIsNone(row['coordinates'])

    def test_unknown_currency_not_inferred_from_locale(self):
        row = klook.parse_page(self.page({'@type': 'Product', 'name': 'Tour',
            'offers': {'price': '42'}}), URL)['offers'][0]
        self.assertEqual(row['prices'], [])

    def test_challenge_not_empty_success(self):
        self.assertEqual(klook.parse_page('captcha-delivery.com', URL)['status'], 'blocked')

    def test_unsupported_not_empty_success(self):
        self.assertEqual(klook.parse_page('<html>shell</html>', URL)['status'], 'unsupported_page')

    def test_urls_reject_search_credentials_redirect_hosts(self):
        for url in ['https://www.klook.com/en-US/search/?query=Sydney',
                    'https://www.klook.com.evil.test/en-AU/activity/123-test/',
                    'http://www.klook.com/en-AU/activity/123-test/']:
            with self.assertRaises(ValueError):
                klook.activity_url(url)

    def test_bad_prices_not_imported(self):
        for value in ['NaN', 'Infinity', -1, True]:
            self.assertIsNone(klook.number(value))


if __name__ == '__main__':
    unittest.main()
