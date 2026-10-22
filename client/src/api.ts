export interface Profile {
  id: string;
  answers: number[];
  values: number[];
  character: string;
  createdAt: string;
}
export interface TripRequest {
  profileId: string;
  title: string;
  date: string;
  startTime: string;
  endTime: string;
  budget: number;
  energy: number;
  theme: string;
  notes: string;
}
export interface Stop {
  id: string;
  name: string;
  category: string;
  startTime: string;
  endTime: string;
  transferMinutes: number;
  durationMinutes: number;
  cost: number;
  walkKm: number;
  remainingHp: number;
  description: string;
}
export interface Plan {
  stops: Stop[];
  remainingHp: number;
  totalCost: number;
  walkKm: number;
  returnTime: string;
  reasons: string[];
  dataNotice: string;
}
export interface TripVersion {
  version: number;
  createdAt: string;
  easier: boolean;
  plan: Plan | null;
  conflicts: string[];
}
export interface TripSummary {
  id: string;
  title: string;
  status: 'completed' | 'infeasible' | 'failed';
  createdAt: string;
  latestVersion: number;
  request: TripRequest;
}
export interface Trip extends TripSummary { versions: TripVersion[]; }
export interface Bootstrap { profile: Profile | null; trips: TripSummary[]; demoData: boolean; }

export async function api<T>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(path, {
    method: body === undefined ? 'GET' : 'POST',
    headers: body === undefined ? { Accept: 'application/json' } : { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  let data: unknown;
  try { data = await response.json(); } catch { throw new Error('服务暂时无法响应，请检查后端是否已启动。'); }
  if (!response.ok) {
    const error = data as { message?: string; errors?: Record<string, string[]> | string[] };
    const details = Array.isArray(error.errors) ? error.errors.join('；') : Object.values(error.errors ?? {}).flat().join('；');
    throw new Error([error.message ?? '请求没有完成，请重试。', details].filter(Boolean).join(' '));
  }
  return data as T;
}
