import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import {
  PLACE_REFERENCES, getPlaceReference, appleMapsSearchUrl,
  appleMapsDirectionsUrl, appleMapsPlaceActions,
} from '../client/src/appleMaps.ts';

let checks = 0;
const check = (name, run) => { run(); checks++; console.log(`PASS ${name}`); };

check('Every planner activity and return has an explicit reference', () => {
  assert.equal(Object.keys(PLACE_REFERENCES).length, 17);
});
const plannerSource = await readFile(new URL('../src/Roam.Core/Planner.cs', import.meta.url), 'utf8');
const plannerIds = [...plannerSource.matchAll(/new\("([a-z]+)",/g)].map(match => match[1]);
check('Planner catalog IDs are all covered by the mapping', () => {
  assert.equal(plannerIds.length, 16);
  assert.deepEqual(plannerIds.filter(id => !Object.hasOwn(PLACE_REFERENCES, id)), []);
});
check('Landmark search points to Apple with the intended Sydney destination', () => {
  const action = appleMapsPlaceActions('harbour', 'walking');
  const search = new URL(action.searchUrl);
  assert.equal(search.origin, 'https://maps.apple.com');
  assert.equal(search.pathname, '/search');
  assert.equal(search.searchParams.get('query'), 'Circular Quay, Sydney NSW, Australia');
  const directions = new URL(action.directionsUrl);
  assert.equal(directions.pathname, '/directions');
  assert.equal(directions.searchParams.get('destination'), 'Circular Quay, Sydney NSW, Australia');
  assert.equal(directions.searchParams.get('mode'), 'walking');
});
check('Chinese addresses, ampersands, plus signs, hashes and query text round-trip safely', () => {
  const text = '悉尼 酒店 A&B + C #12?source=别处';
  const search = new URL(appleMapsSearchUrl(text));
  assert.equal(search.searchParams.get('query'), text);
  assert.equal([...search.searchParams].length, 1);
  const directions = new URL(appleMapsDirectionsUrl(text, 'transit', '码头 & 海港 #1'));
  assert.equal(directions.searchParams.get('destination'), text);
  assert.equal(directions.searchParams.get('source'), '码头 & 海港 #1');
  assert.equal([...directions.searchParams.keys()].sort().join(','), 'destination,mode,source');
});
check('An attempted URL remains search text and never changes the Apple host', () => {
  const text = 'https://example.invalid/?destination=hotel&mode=driving';
  const url = new URL(appleMapsSearchUrl(text));
  assert.equal(url.origin, 'https://maps.apple.com');
  assert.equal(url.searchParams.get('query'), text);
});
check('Whitespace is trimmed and empty searches or destinations produce no link', () => {
  assert.equal(appleMapsSearchUrl(' \n '), null);
  assert.equal(appleMapsDirectionsUrl('\t ', 'walking'), null);
  assert.equal(new URL(appleMapsSearchUrl('  Hyde Park Sydney  ')).searchParams.get('query'), 'Hyde Park Sydney');
});
check('Only the three supported transport modes can generate directions', () => {
  for (const mode of ['walking', 'driving', 'transit']) {
    assert.equal(new URL(appleMapsDirectionsUrl('Sydney', mode)).searchParams.get('mode'), mode);
  }
  for (const mode of ['', 'flying', 'walking&destination=elsewhere', null, undefined]) {
    assert.equal(appleMapsDirectionsUrl('Sydney', mode), null);
  }
});
check('Directions do not invent an origin, hotel, coordinate or Place ID', () => {
  const url = new URL(appleMapsDirectionsUrl('Circular Quay', 'walking'));
  assert.deepEqual([...url.searchParams.keys()].sort(), ['destination', 'mode']);
  assert.equal(new URL(appleMapsDirectionsUrl('Circular Quay', 'walking', '  ')).searchParams.has('source'), false);
});
check('Generic meal and exploration activities can search but cannot navigate to a fake venue', () => {
  for (const id of ['studio', 'market', 'reserve', 'lunch', 'picnic', 'coffee']) {
    const actions = appleMapsPlaceActions(id, 'driving');
    assert.equal(actions.reference.kind, 'search');
    assert.ok(actions.searchUrl);
    assert.equal(actions.directionsUrl, null);
  }
});
check('Unverified, unknown and prototype keys never invent a destination', () => {
  for (const id of ['unverified', 'not-a-place', '__proto__', 'constructor']) {
    const actions = appleMapsPlaceActions(id, 'walking');
    assert.equal(actions.reference.kind, 'unknown');
    assert.equal(actions.searchUrl, null);
    assert.equal(actions.directionsUrl, null);
    assert.equal(getPlaceReference(id).id, id);
  }
});
check('Return navigation requires an explicit user-provided address', () => {
  assert.equal(appleMapsPlaceActions('return', 'walking').directionsUrl, null);
  assert.equal(appleMapsPlaceActions('return', 'walking', '  ').searchUrl, null);
  const actions = appleMapsPlaceActions('return', 'transit', '  我确认的酒店 & 12号  ');
  assert.equal(new URL(actions.directionsUrl).searchParams.get('destination'), '我确认的酒店 & 12号');
  assert.equal(new URL(actions.searchUrl).searchParams.get('query'), '我确认的酒店 & 12号');
});
console.log(`${checks} Apple Maps link checks passed.`);
