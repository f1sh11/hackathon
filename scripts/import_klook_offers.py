#!/usr/bin/env python3
"""Bounded public Klook JSON-LD reader; no challenge bypass or private APIs.
Outputs offers, never venue coordinates, menu prices, or planner budget deductions.
"""
import argparse
from datetime import datetime, timezone
from decimal import Decimal, InvalidOperation
from html.parser import HTMLParser
import json
from pathlib import Path
import re
import ssl
import time
from urllib.error import HTTPError
from urllib.parse import urlsplit
from urllib.request import Request, build_opener, HTTPSHandler, HTTPRedirectHandler
from urllib.robotparser import RobotFileParser

UA = 'SydneyBuddyResearch/1.0'
MAX_BYTES = 5 * 1024 * 1024


def activity_url(value):
    p = urlsplit(value)
    if (p.scheme != 'https' or p.netloc != 'www.klook.com' or p.query or p.fragment
            or not re.fullmatch(r'/[a-z]{2}(?:-[A-Z]{2})?/activity/\d+-[a-zA-Z0-9-]+/', p.path)):
        raise ValueError('Expected a canonical https://www.klook.com/<locale>/activity/<id>-<slug>/ URL')
    return value


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def download(url):
    # Do not follow redirects to login, challenge, or unrelated hosts.
    context = ssl.create_default_context()
    if Path('/etc/ssl/cert.pem').exists():
        context.load_verify_locations('/etc/ssl/cert.pem')
    opener = build_opener(NoRedirect(), HTTPSHandler(context=context))
    with opener.open(Request(url, headers={'User-Agent': UA}), timeout=25) as response:
        raw = response.read(MAX_BYTES + 1)
        if len(raw) > MAX_BYTES:
            raise ValueError('Page exceeds 5 MiB limit')
        return raw.decode('utf-8', errors='replace')


class JsonLd(HTMLParser):
    def __init__(self):
        super().__init__()
        self.active = False
        self.parts = []
        self.blocks = []

    def handle_starttag(self, tag, attrs):
        if tag == 'script' and dict(attrs).get('type', '').lower() == 'application/ld+json':
            self.active = True
            self.parts = []

    def handle_data(self, data):
        if self.active:
            self.parts.append(data)

    def handle_endtag(self, tag):
        if tag == 'script' and self.active:
            self.blocks.append(''.join(self.parts))
            self.active = False


def objects(value):
    if isinstance(value, dict):
        yield value
        for child in value.values():
            yield from objects(child)
    elif isinstance(value, list):
        for child in value:
            yield from objects(child)


def number(value):
    if isinstance(value, bool):
        return None
    try:
        result = Decimal(str(value))
        return float(result) if result.is_finite() and 0 <= result <= 100000000 else None
    except (InvalidOperation, ValueError):
        return None


def parse_page(page, url, observed_at=None):
    activity_url(url)
    if any(x in page.lower() for x in ('captcha-delivery.com', 'please enable js and disable', 'access denied')):
        return {'status': 'blocked', 'sourceUrl': url, 'offers': []}
    parser = JsonLd()
    parser.feed(page)
    rows = []
    seen = set()
    for block in parser.blocks:
        try:
            document = json.loads(block)
        except (ValueError, RecursionError):
            continue
        for obj in objects(document):
            types = obj.get('@type', [])
            if isinstance(types, str):
                types = [types]
            if not isinstance(types, list) or not any(
                    isinstance(t, str) and t in {'Product', 'TouristTrip', 'TouristAttraction'} for t in types):
                continue
            title = obj.get('name')
            if not isinstance(title, str) or not title.strip() or title in seen:
                continue
            seen.add(title)
            offers = obj.get('offers') or []
            if isinstance(offers, dict):
                offers = [offers]
            prices = []
            for offer in offers if isinstance(offers, list) else []:
                if not isinstance(offer, dict):
                    continue
                currency = offer.get('priceCurrency')
                price = number(offer.get('lowPrice', offer.get('price')))
                if price is not None and isinstance(currency, str) and re.fullmatch('[A-Z]{3}', currency):
                    prices.append({'amount': price, 'currency': currency,
                                   'kind': 'advertised_from_price', 'budgetEligible': False})
            rating = obj.get('aggregateRating') or {}
            if not isinstance(rating, dict):
                rating = {}
            score = number(rating.get('ratingValue'))
            best = number(rating.get('bestRating', 5))
            count = number(rating.get('reviewCount', rating.get('ratingCount')))
            rows.append({'id': 'klook:' + re.search(r'/activity/(\d+)', url)[1],
                         'title': title.strip()[:500], 'source': 'Klook', 'sourceUrl': url,
                         'observedAt': observed_at or datetime.now(timezone.utc).isoformat(),
                         'prices': prices, 'rating': score, 'ratingScale': best,
                         'reviewCount': int(count) if count is not None and count.is_integer() else None,
                         'coordinates': None, 'license': 'not established; source reference only'})
    return {'status': 'ok' if rows else 'unsupported_page', 'sourceUrl': url, 'offers': rows}


def collect(url):
    activity_url(url)
    try:
        robots = RobotFileParser()
        robots.parse(download('https://www.klook.com/robots.txt').splitlines())
        if not robots.can_fetch(UA, url):
            return {'status': 'robots_disallowed', 'sourceUrl': url, 'offers': []}
        return parse_page(download(url), url)
    except HTTPError as error:
        return {'status': 'blocked' if error.code in (401, 403, 429) else 'http_error',
                'httpStatus': error.code, 'sourceUrl': url, 'offers': []}
    except (OSError, ValueError) as error:
        return {'status': 'fetch_failed', 'sourceUrl': url, 'offers': [], 'message': str(error)[:300]}


def main():
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument('--url', action='append', required=True, type=activity_url)
    cli.add_argument('--html', type=Path, help='Parse user-supplied saved HTML instead of making requests')
    cli.add_argument('--output', type=Path, required=True)
    args = cli.parse_args()
    if len(args.url) > 5 or (args.html and len(args.url) != 1):
        cli.error('Maximum five URLs; --html requires exactly one URL')
    results = []
    for index, url in enumerate(dict.fromkeys(args.url)):
        if index and not args.html:
            time.sleep(2)
        results.append(parse_page(args.html.read_text(), url) if args.html else collect(url))
        if results[-1]['status'] == 'blocked':
            break  # Stop the entire run on a challenge or rate limit.
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({'schemaVersion': 1, 'results': results}, indent=2) + '\n')
    print(json.dumps({'statuses': [r['status'] for r in results],
                      'offerCount': sum(len(r['offers']) for r in results)}))
    return 0 if all(r['status'] == 'ok' for r in results) else 2


if __name__ == '__main__':
    raise SystemExit(main())
