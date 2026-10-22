import test from 'node:test';
import assert from 'node:assert/strict';
import { defaultProfile, parseCents, savePayload, splitCents, validateProfile } from './domain.ts';
import type { Trip } from './types.ts';

// 资金精度和版本语义是容易出错的业务边界。 / Money precision and revision semantics are meaningful boundary checks.
test('integer cents round half up without floating-point loss', () => {
  assert.equal(parseCents('1.005'), 101);
  assert.equal(parseCents('12,345'), 1235);
  assert.equal(parseCents('0.01'), 1);
  assert.equal(parseCents('99999.99'), 9999999);
  for (const value of ['-2', '0', 'not money', '100000', '99999.999'])
    assert.throws(() => parseCents(value));
});
test('split cents always preserves every cent and allocates remainders', () => {
  assert.deepEqual(splitCents(100, 3), [34, 33, 33]);
  for (let people = 1; people <= 10; people++)
    for (const cents of [1, 99, 100, 10001]) {
      const shares = splitCents(cents, people);
      assert.equal(
        shares.reduce((sum, share) => sum + share, 0),
        cents,
      );
      assert.ok(Math.max(...shares) - Math.min(...shares) <= 1);
    }
  for (const people of [0, 11, 1.5, NaN]) assert.throws(() => splitCents(100, people));
});
test('new proposal cannot accidentally overwrite a trip id carried by a response', () => {
  const trip = {
    id: 'unexpected',
    revision: 99,
    profile: defaultProfile(),
    days: [],
  } as unknown as Trip;
  const payload = savePayload({ trip, expectedRevision: 0 });
  assert.equal(payload.expectedRevision, 0);
  assert.equal(payload.trip.id, undefined);
  assert.equal(payload.trip.revision, undefined);
  assert.equal(trip.id, 'unexpected');
  assert.equal(trip.revision, 99);
});
test('adjustment saves only to the captured target id and expected revision', () => {
  const trip = {
    id: 'model-id',
    revision: 42,
    profile: defaultProfile(),
    days: [],
  } as unknown as Trip;
  const payload = savePayload({ trip, targetId: 'saved-id', expectedRevision: 3 });
  assert.equal(payload.trip.id, 'saved-id');
  assert.equal(payload.expectedRevision, 3);
  assert.equal(payload.trip.revision, undefined);
});
test('invalid timing, budget and step limits are rejected before sending', () => {
  assert.doesNotThrow(() => validateProfile(defaultProfile()));
  for (const change of [
    { startMinute: 1000, endMinute: 900 },
    { maxSteps: 499 },
    { days: 4 },
    { budget: NaN },
  ])
    assert.throws(() => validateProfile({ ...defaultProfile(), ...change }));
});
