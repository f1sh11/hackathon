#!/usr/bin/env python3
"""Sync only published community place records to the separate LOCAL Zetaris source DB.
No social network crawling, account cookies, raw post bodies, or weather are exported.
"""
import argparse,csv,io,json,subprocess,sys,uuid
from pathlib import Path
from urllib.request import Request,urlopen
ROOT=Path(__file__).resolve().parents[1]
def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--platform-env',type=Path,required=True)
    parser.add_argument('--source-env',type=Path,default=ROOT/'infra/zetaris/.env.local')
    parser.add_argument('--register',action='store_true')
    args=parser.parse_args()
    with urlopen('http://127.0.0.1:8787/social/published',timeout=30) as response:places=json.load(response)['places']
    if len(places)>1000 or any(not p['id'].startswith('social:') for p in places):raise ValueError('Invalid collection')
    # CSV quoting prevents captions from becoming SQL. The SQL itself is fixed.
    buffer=io.StringIO();writer=csv.writer(buffer)
    for p in places:writer.writerow([p['id'],json.dumps(p,ensure_ascii=False)])
    sql='''BEGIN;
CREATE TABLE IF NOT EXISTS social_place_records(id text PRIMARY KEY,payload text NOT NULL CHECK(jsonb_typeof(payload::jsonb)='object'));
CREATE OR REPLACE VIEW travel_social_places AS SELECT id,payload FROM social_place_records;
GRANT SELECT ON travel_social_places TO travel_reader;
CREATE TEMP TABLE social_import_batch(id text PRIMARY KEY,payload text) ON COMMIT DROP;
COPY social_import_batch(id,payload) FROM STDIN WITH(FORMAT csv);
'''+buffer.getvalue()+'''\\.
DELETE FROM social_place_records;
INSERT INTO social_place_records SELECT * FROM social_import_batch;
COMMIT;
'''
    command=['docker','compose','--env-file',str(args.source_env),'-f',str(ROOT/'infra/zetaris/compose.source.yaml'),'exec','-T','source-db','psql','-U','travel_source_admin','-d','travel_public','-v','ON_ERROR_STOP=1']
    subprocess.run(command,input=sql,text=True,capture_output=True,check=True)
    env={}
    for line in args.platform_env.read_text().splitlines():
        key,sep,value=line.partition('=')
        if sep and not key.startswith('#'):env[key.strip()]=value.strip().strip('\"\'')
    def post(url,body,headers=None):
        request=Request(url,data=json.dumps(body).encode(),headers={'Content-Type':'application/json',**(headers or {})})
        with urlopen(request,timeout=60) as response:return json.load(response)
    auth=post('http://127.0.0.1:3000/api/auth/login',{'username':env['ZETARIS_ADMIN_EMAIL'],'password':env['ZETARIS_ADMIN_PASSWORD']})
    def query(statement):return post('http://127.0.0.1:8888/api/v1.0/sql-editor/sqls/run',{'queryId':str(uuid.uuid4()),'sql':statement,'source':'SqlEditor','limit':20000},{'Authorization':'Bearer '+auth['idToken'],'X-Org-ID':str(auth['orgId']),'X-Request-ID':str(uuid.uuid4())})
    if args.register:
        tables=query('SHOW TABLES FROM sydneybuddy_travel')['data']
        if not any(row[0].lower()=='sydneybuddy_travel.travel_social_places' for row in tables):query('REGISTER DATASOURCE TABLE "travel_social_places" FROM sydneybuddy_travel')
    query('UNCACHE TABLE sydneybuddy_travel.travel_social_places')
    result=query('SELECT payload FROM sydneybuddy_travel.travel_social_places')
    actual=[json.loads(row[0]) for row in result['data']]
    if {p['id'] for p in actual}!={p['id'] for p in places}:raise ValueError('Zetaris collection mismatch')
    print(json.dumps({'status':'verified','publishedPlaces':len(actual),'realZetaris':True,'rawPostsExported':False}))
if __name__=='__main__':
    try:main()
    except Exception as error:
        print(json.dumps({'status':'failed','errorType':type(error).__name__}),file=sys.stderr);sys.exit(1)
