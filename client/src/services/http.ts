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
