/** 与 ASP.NET API 一致的网络数据结构；不包含密钥。 / Wire contracts shared with ASP.NET API; never include secrets. */
export type Mode = 'demo' | 'assisted' | 'live';
export interface Endpoint {
  name: string;
  lat: number;
  lng: number;
}
export interface Profile {
  date: string;
  days: number;
  budget: number;
  maxSteps: number;
  startMinute: number;
  endMinute: number;
  interests: string[];
  mustVisit: string[];
  start: Endpoint;
  end: Endpoint;
  request?: string;
  weatherOverride?: string;
}
export interface Place extends Endpoint {
  id: string;
  category: string;
  indoor: boolean;
  visitMinutes: number;
  cost: number;
  note: string;
  mapsURL?: string;
}
export interface Stop extends Place {
  arrival: string;
  departure: string;
  hoursVerified: boolean;
  steps: number;
}
export interface Leg {
  meters: number;
  minutes: number;
  polyline: string;
  source: string;
}
export interface TripDay {
  date: string;
  stops: Stop[];
  legs: Leg[];
  estimatedCost: number;
  estimatedSteps: number;
  finishTime: string;
  warnings: string[];
  start: Endpoint;
  end: Endpoint;
}
export interface ForecastDay {
  date: string;
  summary: string;
  rainy: boolean;
  available: boolean;
  temperatureMin?: number;
  temperatureMax?: number;
  precipitationProbability?: number;
}
export interface Weather {
  summary: string;
  rainy: boolean;
  source: string;
  status?: string;
  days?: ForecastDay[];
  fetchedAt?: string;
}
export interface Trace {
  title: string;
  detail: string;
  source: string;
  fetchedAt: string;
}
export interface Trip {
  id?: string;
  revision?: number;
  title: string;
  mode: Mode;
  profile: Profile;
  days: TripDay[];
  weather: Weather;
  explanation: string;
  warnings: string[];
  trace: Trace[];
  createdAt: string;
}
export interface Proposal {
  trip: Trip;
  targetId?: string;
  expectedRevision: number;
}
export interface Memory {
  id: string;
  summary: string;
  profile: Pick<Profile, 'interests' | 'maxSteps'>;
  scope: string;
  consent: boolean;
  provenance: { source: string; createdAt: string };
}
export interface Health {
  status: string;
  nvidiaConfigured: boolean;
  googleConfigured: boolean;
  iosMapConfigured: boolean;
  model: string;
}
export interface Expense {
  id: string;
  title: string;
  cents: number;
  people: number;
}
