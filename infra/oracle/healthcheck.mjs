try {
  const response = await fetch('http://127.0.0.1:8787/health', {signal: AbortSignal.timeout(4000)});
  const body = await response.json();
  process.exit(response.ok && body.status === 'ok' ? 0 : 1);
} catch { process.exit(1); }
