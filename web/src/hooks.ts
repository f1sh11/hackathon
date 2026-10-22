import { useEffect, useState } from 'react';

/** 只负责浏览器本机状态，不代表服务端保存。 / Persists browser-only state, never server records. */
export function useLocalState<T>(key: string, fallback: T) {
  const [value, setValue] = useState<T>(() => {
    try {
      const raw = localStorage.getItem(key);
      return raw ? (JSON.parse(raw) as T) : fallback;
    } catch {
      return fallback;
    }
  });
  useEffect(() => {
    try {
      localStorage.setItem(key, JSON.stringify(value));
    } catch {
      /* Private mode can deny storage. / 隐私模式可能禁止存储。 */
    }
  }, [key, value]);
  return [value, setValue] as const;
}
