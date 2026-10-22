/** 纯业务函数：偏好验证、草案版本和金额计算。 / Pure domain helpers for preferences, revisions and money. */
import type { Endpoint, Profile, Proposal, Trip } from './types.ts';

export const ENDPOINTS: Endpoint[] = [
  { name: '环形码头', lat: -33.8611, lng: 151.2126 },
  { name: '中央车站', lat: -33.883, lng: 151.2065 },
];
export const INTERESTS = ['风景', '美食', '自然', '艺术', '城市', '文化'];

export function sydneyToday() {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Australia/Sydney',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date());
  const part = (type: string) => parts.find((p) => p.type === type)?.value ?? '';
  return `${part('year')}-${part('month')}-${part('day')}`;
}
export function defaultProfile(): Profile {
  return {
    date: sydneyToday(),
    days: 1,
    budget: 100,
    maxSteps: 10_000,
    startMinute: 600,
    endMinute: 1080,
    interests: ['风景', '美食'],
    mustVisit: [],
    start: { ...ENDPOINTS[0] },
    end: { ...ENDPOINTS[0] },
  };
}
export function validateProfile(p: Profile) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(p.date) || Number.isNaN(Date.parse(`${p.date}T12:00:00Z`)))
    throw new Error('请选择有效日期');
  if (!Number.isInteger(p.days) || p.days < 1 || p.days > 3) throw new Error('行程支持 1–3 天');
  if (!Number.isFinite(p.budget) || p.budget < 0 || p.budget > 100_000)
    throw new Error('每日活动预算须在 A$0–100,000 之间');
  if (!Number.isInteger(p.maxSteps) || p.maxSteps < 500 || p.maxSteps > 60_000)
    throw new Error('每日步数须在 500–60,000 之间');
  if (
    !Number.isInteger(p.startMinute) ||
    !Number.isInteger(p.endMinute) ||
    p.startMinute < 0 ||
    p.endMinute > 1440 ||
    p.startMinute >= p.endMinute
  )
    throw new Error('返回时间须晚于出发时间');
  if ((p.request?.length ?? 0) > 1500) throw new Error('调整要求请控制在 1500 字以内');
}
export function savePayload(proposal: Proposal) {
  // 草案不能携带模型给出的 id/revision 覆盖其他行程。 / A proposal may not use model-supplied ids or revisions to overwrite another trip.
  const trip = structuredClone(proposal.trip);
  delete trip.id;
  delete trip.revision;
  if (proposal.targetId) trip.id = proposal.targetId;
  return { trip, expectedRevision: proposal.expectedRevision };
}
export function parseCents(raw: string) {
  // 直接从十进制文本取整，避免 1.005 * 100 的二进制浮点误差。 / Parse decimal text directly to avoid binary floating-point rounding errors.
  const normalized = raw.trim().replace(',', '.');
  if (!/^\d+(?:\.\d*)?$/.test(normalized)) throw new Error('请输入有效金额');
  const [whole, fraction = ''] = normalized.split('.');
  const dollars = Number(whole);
  if (!Number.isSafeInteger(dollars) || dollars >= 100_000) throw new Error('金额须小于 A$100,000');
  const cents =
    dollars * 100 +
    Number((fraction + '00').slice(0, 2)) +
    (Number(fraction[2] ?? '0') >= 5 ? 1 : 0);
  if (cents <= 0 || cents >= 10_000_000) throw new Error('金额须至少 A$0.01 且小于 A$100,000');
  return cents;
}
export function splitCents(cents: number, people: number) {
  if (
    !Number.isSafeInteger(cents) ||
    cents <= 0 ||
    !Number.isInteger(people) ||
    people < 1 ||
    people > 10
  )
    throw new Error('金额须为正数，分摊人数支持 1–10 人');
  return Array.from(
    { length: people },
    (_, i) => Math.floor(cents / people) + (i < cents % people ? 1 : 0),
  );
}
/** 拒绝无法展示的历史记录，而不是隐藏或改写它。 / Reject unrenderable historical records without hiding or modifying them. */
export function isTrip(raw: unknown): raw is Trip {
  if (!raw || typeof raw !== 'object') return false;
  const t = raw as Partial<Trip>;
  if (
    !t.profile ||
    !Array.isArray(t.days) ||
    !Array.isArray(t.trace) ||
    !Array.isArray(t.warnings) ||
    !t.weather
  )
    return false;
  if (
    !Array.isArray(t.profile.interests) ||
    !Array.isArray(t.profile.mustVisit) ||
    !t.profile.start ||
    !t.profile.end
  )
    return false;
  if (
    typeof t.createdAt !== 'string' ||
    typeof t.title !== 'string' ||
    !['demo', 'assisted', 'live'].includes(t.mode ?? '')
  )
    return false;
  return t.days.every(
    (d) =>
      typeof d.date === 'string' &&
      Array.isArray(d.stops) &&
      Array.isArray(d.legs) &&
      Array.isArray(d.warnings) &&
      !!d.start &&
      !!d.end &&
      Number.isFinite(d.estimatedSteps) &&
      Number.isFinite(d.estimatedCost) &&
      d.stops.every(
        (s) =>
          typeof s.id === 'string' &&
          typeof s.name === 'string' &&
          Number.isFinite(s.lat) &&
          Number.isFinite(s.lng),
      ),
  );
}
export function money(cents: number) {
  return `A$${(cents / 100).toFixed(2)}`;
}
export function clock(minutes: number) {
  return `${Math.floor(minutes / 60)
    .toString()
    .padStart(2, '0')}:${(minutes % 60).toString().padStart(2, '0')}`;
}
export function parseClock(value: string) {
  const [hour, minute] = value.split(':').map(Number);
  return hour * 60 + minute;
}
export function shortDate(date: string) {
  return new Intl.DateTimeFormat('zh-CN', {
    month: 'long',
    day: 'numeric',
    weekday: 'short',
    timeZone: 'Australia/Sydney',
  }).format(new Date(`${date}T12:00:00+11:00`));
}
export function tripSteps(trip: Trip) {
  return trip.days.reduce((sum, d) => sum + d.estimatedSteps, 0);
}
export function stopIds(trip: Trip) {
  return trip.days.flatMap((d) => d.stops.map((s) => s.id));
}
export function mapLink(place: Endpoint & { mapsURL?: string }) {
  if (place.mapsURL) {
    try {
      const u = new URL(place.mapsURL);
      if (
        u.protocol === 'https:' &&
        (u.hostname === 'maps.google.com' ||
          u.hostname === 'www.google.com' ||
          u.hostname === 'maps.app.goo.gl')
      )
        return u.href;
    } catch {
      /* use coordinates */
    }
  }
  return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(`${place.lat},${place.lng}`)}`;
}
