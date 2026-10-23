/**
 * Optional LOCAL DEVELOPMENT helper for macOS .NET / public-demo TLS incompatibility.
 * Uses Node's normal certificate validation. Binds only to loopback and forwards only
 * /route to a fixed public Valhalla demo. Not a production service or offline router.
 */
import http from 'node:http';

let inFlight = 0;
const server = http.createServer(async (request, response) => {
  const reply = (status, value) => {
    response.writeHead(status, { 'content-type': 'application/json; charset=utf-8' });
    response.end(JSON.stringify(value));
  };
  if (request.method !== 'POST' || request.url !== '/route') return reply(404, { error: 'Use POST /route' });
  if (inFlight >= 2) return reply(429, { error: 'Demo request already in progress; please retry later.' });
  inFlight++;
  try {
    const chunks = [];
    let size = 0;
    for await (const chunk of request) {
      size += chunk.length;
      if (size > 64_000) { reply(413, { error: 'Request too large' }); return; }
      chunks.push(chunk);
    }
    let body;
    try { body = JSON.parse(Buffer.concat(chunks).toString('utf8')); }
    catch { reply(400, { error: 'Invalid JSON' }); return; }
    if (!Array.isArray(body.locations) || body.locations.length < 2 || body.locations.length > 30) {
      reply(422, { error: 'Expected 2–30 locations' }); return;
    }
    const upstream = await fetch('https://valhalla1.openstreetmap.de/route', {
      method: 'POST', redirect: 'error', signal: AbortSignal.timeout(18_000),
      headers: { 'content-type': 'application/json', 'user-agent': 'SydneyBuddy/1.0 (local development; https://github.com/f1sh11/hackathon)' },
      body: JSON.stringify(body),
    });
    const result = await upstream.json();
    reply(upstream.status, result);
  } catch {
    reply(502, { error: 'Public Valhalla demo unavailable. Retry later or use your own routing server.' });
  } finally { inFlight--; }
});
server.requestTimeout = 25_000;
server.listen(8002, '127.0.0.1', () => console.log('Development only: http://127.0.0.1:8002/route → public Valhalla demo. No availability guarantee.'));
