#!/usr/bin/env python3
"""Register bounded public traffic/fee REST tables on LOCAL Zetaris. No private keys in SQL/output."""
import argparse,json,sys,uuid
from pathlib import Path
from urllib.request import Request,urlopen
from urllib.error import HTTPError
ROOT=Path(__file__).resolve().parents[1]
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--platform-env',type=Path,required=True);p.add_argument('--register',action='store_true');p.add_argument('--verify-kind',choices=['traffic','prices','all'],default='all');a=p.parse_args()
 env={}
 for line in a.platform_env.read_text().splitlines():
  key,sep,value=line.partition('=')
  if sep and not key.startswith('#'):env[key.strip()]=value.strip().strip('\"\'')
 def post(url,body,headers={}):
  try:
   with urlopen(Request(url,data=json.dumps(body).encode(),headers={'Content-Type':'application/json',**headers}),timeout=120) as response:return json.load(response)
  except HTTPError as error:
   raise
 auth=post('http://127.0.0.1:3000/api/auth/login',{'username':env['ZETARIS_ADMIN_EMAIL'],'password':env['ZETARIS_ADMIN_PASSWORD']})
 def query(sql):return post('http://127.0.0.1:8888/api/v1.0/sql-editor/sqls/run',{'queryId':str(uuid.uuid4()),'sql':sql,'source':'SqlEditor','limit':20000},{'Authorization':'Bearer '+auth['idToken'],'X-Org-ID':str(auth['orgId']),'X-Request-ID':str(uuid.uuid4())})
 sources=json.loads((ROOT/'shared/travel-insight-sources.json').read_text());tables=query('SHOW TABLES FROM sydneybuddy_live')['data']
 evidence={}
 for kind,source in sources.items():
  table=source['table']
  if a.register and not any(row[0].lower()=='sydneybuddy_live.'+table for row in tables):
   query('CREATE LIGHTNING REST TABLE '+table+' FROM sydneybuddy_live REQUEST(endpoint "'+source['endpoint']+'",method "get",response_type "json",http_encoding "URLENCODED") HEADER () BODY ()')
  if a.verify_kind not in ('all',kind):continue
  try:
   data=query('SELECT to_json(struct(*)) AS payload FROM sydneybuddy_live.'+table)
   if len(data['data'])!=1:raise ValueError('Response cardinality')
   raw=json.loads(data['data'][0][0]);field='features' if kind=='traffic' else 'elements'
   if not isinstance(raw.get(field),list):raise ValueError('Missing collection')
   evidence[kind]={'status':'verified','records':len(raw[field]),'via':'native Zetaris REST','source':source['source']}
  except Exception as error:evidence[kind]={'status':'unavailable','errorType':type(error).__name__}
 print(json.dumps(evidence))
 if any(e['status']!='verified' for e in evidence.values()):sys.exit(2)
if __name__=='__main__':
 try:main()
 except Exception as error:print(json.dumps({'status':'failed','errorType':type(error).__name__,'httpStatus':getattr(error,'code',None)}),file=sys.stderr);sys.exit(1)
