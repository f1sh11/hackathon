"""Loopback-only development API. No cloud deployment, identity or multi-user access implied."""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import datetime as dt
import json
import os
import secrets
import sqlite3
import threading
import uuid
from urllib.parse import urlparse
from planner import Planner, ServiceError, credentials, CATALOG
from memory import run as memory_run

ROOT = Path(__file__).resolve().parent
DB = ROOT / 'state.sqlite3'
LOCK = threading.Lock()
SETUP_TOKEN = secrets.token_urlsafe(32)

def connect():
    c = sqlite3.connect(DB)
    c.execute('CREATE TABLE IF NOT EXISTS trips(id TEXT PRIMARY KEY, data TEXT NOT NULL, revision INTEGER NOT NULL)')
    c.execute('CREATE TABLE IF NOT EXISTS memories(id TEXT PRIMARY KEY, data TEXT NOT NULL)')
    return c

def setup_html():
    status = credentials()
    fields = ''.join(f'<label>{label}<input type="password" name="{key}" autocomplete="off" placeholder="'+('Configured; leave blank to keep' if status.get(key) else 'Not configured')+'"></label>' for key,label in [
        ('NVIDIA_API_KEY','NVIDIA API key (server only)'),('GOOGLE_SERVER_KEY','Google server key (Places API + Routes API only)'),('GOOGLE_IOS_KEY','Google iOS key (Maps SDK for iOS only; restrict by bundle ID)')])
    return '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Sydney Buddy local setup</title><style>body{font:16px system-ui;background:#f8f7ef;color:#153e35;max-width:640px;margin:60px auto;padding:24px}h1{font-size:32px}label{display:block;margin:24px 0}input{display:block;box-sizing:border-box;width:100%;padding:15px;border-radius:12px;border:1px solid #bcd8cb;margin-top:8px}button{background:#138564;color:white;border:0;border-radius:12px;padding:16px;font-size:16px}p{line-height:1.7}small{color:#54675f}</style><h1>🧭 Sydney Buddy</h1><p>Local server settings. Keys are saved only in local files excluded from Git; they are never returned to the browser or written to logs. Do not send keys in chat.</p><form method="post" action="/setup"><input type="hidden" name="token" value="'''+SETUP_TOKEN+'">'+fields+'''<button>Save to local project</button></form><p><small>Enable billing and the required Google APIs first. The iOS key is saved to Config/Secrets.xcconfig; rebuild the app after changing it. The NVIDIA key stays on the server. This service listens on 127.0.0.1 only.</small></p></html>'''

class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        pass
    def respond(self, value, status=200, html=False):
        body = value.encode() if html else json.dumps(value,ensure_ascii=False).encode()
        self.send_response(status)
        self.send_header('Content-Type','text/html; charset=utf-8' if html else 'application/json; charset=utf-8')
        self.send_header('Content-Length',str(len(body)))
        self.send_header('Cache-Control','no-store')
        self.send_header('X-Content-Type-Options','nosniff')
        self.send_header('Content-Security-Policy',"default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; frame-ancestors 'none'")
        self.end_headers();self.wfile.write(body)
    def allowed(self):
        host=self.headers.get('Host','').split(':')[0]
        origin=self.headers.get('Origin')
        return host in ('127.0.0.1','localhost') and (not origin or origin in ('http://127.0.0.1:8787','http://localhost:8787'))
    def body(self):
        n=int(self.headers.get('Content-Length','0'))
        if not 0<n<=250000:
            raise ServiceError('Invalid request size')
        return json.loads(self.rfile.read(n))
    def do_GET(self):
        if not self.allowed():
            return self.respond({'error':'Local requests only'},403)
        path=urlparse(self.path).path
        if path=='/setup':
            return self.respond(setup_html(),html=True)
        if path=='/health':
            config=credentials()
            return self.respond({'status':'ok','nvidiaConfigured':bool(config['NVIDIA_API_KEY']),
                'googleConfigured':bool(config['GOOGLE_SERVER_KEY']),'iosMapConfigured':bool(config['GOOGLE_IOS_KEY']),
                'model':config.get('NVIDIA_MODEL') or 'nvidia/nemotron-3.5-lightning-30b-a3b'})
        if path=='/catalog':
            return self.respond(CATALOG)
        if path in ('/trips','/memories'):
            with connect() as c:
                rows=c.execute('SELECT data FROM '+('trips' if path=='/trips' else 'memories')).fetchall()
            return self.respond([json.loads(r[0]) for r in rows])
        return self.respond({'error':'Not found'},404)
    def do_POST(self):
        if not self.allowed():
            return self.respond({'error':'Local requests only'},403)
        path=urlparse(self.path).path
        try:
            if path=='/setup':
                from urllib.parse import parse_qs
                n=int(self.headers.get('Content-Length','0'))
                if n>10000:return self.respond({'error':'Request too large'},400)
                form=parse_qs(self.rfile.read(n).decode())
                if not secrets.compare_digest(form.get('token',[''])[0],SETUP_TOKEN):
                    return self.respond({'error':'This setup page has expired. Refresh and try again.'},403)
                current=credentials()
                for k in ['NVIDIA_API_KEY','GOOGLE_SERVER_KEY','GOOGLE_IOS_KEY']:
                    value=form.get(k,[''])[0].strip()
                    if value:
                        if not all(ch.isalnum() or ch in '_-.' for ch in value):
                            raise ServiceError('Invalid key format')
                        current[k]=value
                p=ROOT.parent/'.secrets.json'
                fd=os.open(p,os.O_WRONLY|os.O_CREAT|os.O_TRUNC,0o600)
                with os.fdopen(fd,'w') as f:json.dump(current,f)
                if current.get('GOOGLE_IOS_KEY'):
                    target=ROOT.parent/'ios/Config/Secrets.xcconfig'
                    fd=os.open(target,os.O_WRONLY|os.O_CREAT|os.O_TRUNC,0o600)
                    with os.fdopen(fd,'w') as f:f.write('GOOGLE_MAPS_API_KEY = '+current['GOOGLE_IOS_KEY']+'\n')
                return self.respond('<meta charset="utf-8"><h1>Saved locally</h1><p>You can close this page. Rebuild the iOS app after changing its Maps key.</p>',html=True)
            data=self.body()
            if path=='/weather':
                from planner import validate_input
                return self.respond(Planner('live').weather(validate_input(data)))
            if path=='/plan':
                mode=data.pop('mode','demo')
                if mode not in ('demo','assisted','live'):raise ServiceError('Invalid mode')
                return self.respond(Planner(mode).plan(data))
            if path=='/trips':
                trip=data['trip'];ident=trip.get('id') or str(uuid.uuid4())
                with LOCK,connect() as c:
                    row=c.execute('SELECT revision FROM trips WHERE id=?',(ident,)).fetchone()
                    revision=row[0] if row else 0
                    if data.get('expectedRevision',0)!=revision:
                        return self.respond({'error':'This itinerary has changed. Reload it before confirming.'},409)
                    trip.update(id=ident,revision=revision+1)
                    c.execute('INSERT OR REPLACE INTO trips VALUES(?,?,?)',(ident,json.dumps(trip,ensure_ascii=False),revision+1))
                return self.respond(trip)
            if path=='/memories':
                if data.get('consent') is not True:raise ServiceError('Your explicit consent is required to save a memory.')
                profile=data.get('profile',{})
                summary=str(data.get('summary',''))[:500]
                record=memory_run('add',content='travel preference Sydney: '+summary)
                memory={'id':record['id'],'summary':summary,
                        'profile':{'interests':profile.get('interests',[]),'maxSteps':profile.get('maxSteps',10000)},
                        'provenance':{'source':'user_confirmed','createdAt':dt.datetime.now(dt.timezone.utc).isoformat()},
                        'scope':'local_only','consent':True}
                with connect() as c:c.execute('INSERT INTO memories VALUES(?,?)',(memory['id'],json.dumps(memory,ensure_ascii=False)))
                return self.respond(memory)
            return self.respond({'error':'Not found'},404)
        except ServiceError as e:
            return self.respond({'error':str(e)},422)
        except (KeyError,ValueError,TypeError):
            return self.respond({'error':'Invalid request format'},400)
        except Exception:
            return self.respond({'error':'The request failed. Your original itinerary has not changed.'},500)
    def do_DELETE(self):
        if not self.allowed():return self.respond({'error':'Local requests only'},403)
        parts=urlparse(self.path).path.strip('/').split('/')
        if len(parts)!=2 or parts[0] not in ('trips','memories'):
            return self.respond({'error':'Not found'},404)
        if parts[0]=='memories':
            try:memory_run('delete',id=parts[1])
            except ServiceError as e:return self.respond({'error':str(e)},422)
        with connect() as c:c.execute('DELETE FROM '+parts[0]+' WHERE id=?',(parts[1],))
        return self.respond({'deleted':True})

if __name__=='__main__':
    connect().close()
    print('Sydney Buddy API: http://127.0.0.1:8787 | settings: /setup',flush=True)
    ThreadingHTTPServer(('127.0.0.1',8787),Handler).serve_forever()
