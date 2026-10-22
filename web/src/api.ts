/** 网络边界：统一超时、JSON 错误与行程形状检查。 / Network boundary for timeouts, JSON errors and trip validation. */
import type { Health, Memory, Place, Profile, Trip, Weather } from './types';
import { isTrip } from './domain';
/** 保留 HTTP 状态，尤其是乐观并发的 409。 / Retains HTTP status, especially optimistic-concurrency 409. */
export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}
async function request<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 240_000);
  try {
    const response = await fetch(`/api/${path}`, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: controller.signal,
      redirect: 'error',
    });
    const text = await response.text();
    let value: unknown;
    try {
      value = JSON.parse(text);
    } catch {
      throw new ApiError(
        response.status,
        '服务返回了无法识别的数据，请确认 ASP.NET Core 后端已启动',
      );
    }
    if (!response.ok) {
      const error =
        value && typeof value === 'object' && 'error' in value
          ? String(value.error)
          : `服务返回 HTTP ${response.status}`;
      throw new ApiError(
        response.status,
        response.status === 409 ? '行程版本已变化。请取消草案并刷新行程，再重新调整。' : error,
      );
    }
    return value as T;
  } catch (error) {
    if (error instanceof ApiError) throw error;
    if (controller.signal.aborted) throw new Error('请求超时，请稍后重试。已保存的行程没有改变。');
    throw new Error('无法连接旅行服务。请确认后端和数据库正在运行，再点击重新连接。');
  } finally {
    clearTimeout(timeout);
  }
}
export const api = {
  health: () => request<Health>('health'),
  catalog: () => request<Place[]>('catalog'),
  trips: async () => {
    const value = await request<unknown>('trips');
    if (!Array.isArray(value) || !value.every(isTrip))
      throw new Error('已保存的行程数据格式不完整，请检查服务端记录后再刷新。原数据没有改动。');
    return value;
  },
  memories: () => request<Memory[]>('memories'),
  plan: (profile: Profile, mode: string) => request<Trip>('plan', 'POST', { ...profile, mode }),
  weather: (profile: Profile) => request<Weather>('weather', 'POST', profile),
  save: (payload: { trip: Trip; expectedRevision: number }) =>
    request<Trip>('trips', 'POST', payload),
  deleteTrip: (id: string) => request('trips/' + encodeURIComponent(id), 'DELETE'),
  remember: (profile: Profile, summary: string) =>
    request<Memory>('memories', 'POST', { consent: true, profile, summary }),
  deleteMemory: (id: string) => request('memories/' + encodeURIComponent(id), 'DELETE'),
};
