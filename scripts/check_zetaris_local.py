#!/usr/bin/env python3
"""Read-only checks against a REAL local Zetaris instance. Never starts a mock server.
Use --env-file infra/zetaris/.env.local. Values and server response bodies are never logged.
"""
import argparse
import json
import os
from pathlib import Path
import re
import uuid
from urllib.parse import urlparse
from urllib.request import Request, urlopen, HTTPRedirectHandler, build_opener
from urllib.error import HTTPError, URLError

class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self,*args,**kwargs):return None

def load_env(path):
    if not path.is_file():return
    for line in path.read_text().splitlines():
        line=line.strip()
        if not line or line.startswith('#'):continue
        key,sep,value=line.partition('=')
        if sep and re.fullmatch(r'[A-Z_]+',key):os.environ.setdefault(key,value.strip().strip('\"\''))

def check(base,org,key,manifest=None,opener=None):
    address=urlparse(base)
    if address.scheme!='http' or address.hostname not in {'127.0.0.1','localhost','::1'} or address.username or address.password or address.query or address.fragment or address.path.rstrip('/')!='/api/v1.0':
        raise ValueError('Use a local HTTP API base ending in /api/v1.0')
    if not re.fullmatch(r'[0-9]+',org) or not key:raise ValueError('Set numeric ZETARIS_ORG_ID and private ZETARIS_API_KEY')
    opener=opener or build_opener(NoRedirect())
    def call(path,sql=None):
        body=None if sql is None else json.dumps({'queryId':str(uuid.uuid4()),'sql':sql,'source':'SqlEditor','limit':100}).encode()
        request=Request(base.rstrip('/')+path,data=body,headers={'Authorization':'Bearer '+key,'X-Org-ID':org,'X-Request-ID':str(uuid.uuid4()),'Content-Type':'application/json'},method='GET' if body is None else 'POST')
        with opener.open(request,timeout=20) as response:return json.load(response)
    call('/datasource/datasources')
    def run(sql):
        result=call('/sql-editor/sqls/run',sql)
        if not isinstance(result,dict) or not isinstance(result.get('data'),list):raise ValueError('Unexpected Zetaris query response; verify the installed OpenAPI contract')
        return result['data']
    if run('SELECT 1') not in ([[1]],[['1']]):raise ValueError('SELECT 1 did not return 1')
    run('SHOW LIGHTNING DATABASES')
    counts={}
    for kind,table in [('poi','travel_poi'),('catalog','travel_catalog'),('map_layers','travel_map_layers'),('metadata','travel_metadata')]:
        rows=run('SELECT COUNT(*) FROM sydneybuddy_travel.'+table)
        if len(rows)!=1 or not isinstance(rows[0],list) or len(rows[0])!=1:raise ValueError('Unexpected count result')
        counts[kind]=int(rows[0][0])
        if manifest and counts[kind]!=manifest['counts'].get(kind,0):raise ValueError('Source count mismatch: '+kind)
    return {'status':'verified','realZetaris':True,'counts':counts,'checkedAt':__import__('datetime').datetime.now(__import__('datetime').timezone.utc).isoformat()}

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--env-file',type=Path,default=Path(__file__).resolve().parents[1]/'infra/zetaris/.env.local');parser.add_argument('--manifest',type=Path);args=parser.parse_args()
    load_env(args.env_file)
    required=['ZETARIS_REST_URL','ZETARIS_ORG_ID','ZETARIS_API_KEY'];missing=[k for k in required if not os.getenv(k)]
    if missing:print(json.dumps({'status':'not_configured','missing':missing,'realZetaris':False}));return 2
    try:
        manifest=json.loads(args.manifest.read_text()) if args.manifest else None
        print(json.dumps(check(*(os.environ[k] for k in required),manifest),ensure_ascii=False));return 0
    except HTTPError as error:print(json.dumps({'status':'failed','httpStatus':error.code,'realZetaris':False}));return 1
    except (OSError,URLError,ValueError,KeyError,TypeError):print(json.dumps({'status':'failed','message':'Check local installation, API contract, access and source registration.','realZetaris':False}));return 1
if __name__=='__main__':raise SystemExit(main())
