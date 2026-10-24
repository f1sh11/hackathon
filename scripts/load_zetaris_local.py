#!/usr/bin/env python3
"""Register/verify public travel views on a real local Zetaris, via its official JDBC JAR.
Credentials are read from private files and never printed. Default is read-only verification.
"""
import argparse
import csv
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import sys

ROOT=Path(__file__).resolve().parents[1]
TABLES={'poi':'travel_poi','catalog':'travel_catalog','map_layers':'travel_map_layers','metadata':'travel_metadata'}

def env_file(path):
    values={}
    for line in path.read_text().splitlines():
        line=line.strip()
        if line and not line.startswith('#'):
            key,sep,value=line.partition('=')
            if sep:values[key]=value.strip().strip('\"\'')
    return values

def literal(value):
    if any(c in value for c in ['\n','\r','\x00']):raise ValueError('Invalid credential format')
    return '"'+value.replace('\\','\\\\').replace('"','\\"')+'"'

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--platform-env',type=Path,required=True)
    parser.add_argument('--jdbc-jar',type=Path,required=True)
    parser.add_argument('--source-env',type=Path,default=ROOT/'infra/zetaris/.env.local')
    parser.add_argument('--manifest',type=Path,default=ROOT/'infra/zetaris/data/manifest.json')
    parser.add_argument('--register',action='store_true',help='Create only the known public-data source and missing views')
    parser.add_argument('--cache',action='store_true',help='Cache the five small public views to avoid observed JDBC filter/count/limit pushdown errors')
    parser.add_argument('--records',type=Path,default=ROOT/'infra/zetaris/data/travel-records.csv')
    parser.add_argument('--output',type=Path,help='Save public verification results; no credentials or SQL with passwords')
    args=parser.parse_args()
    secrets=[]
    try:
        import jaydebeapi
        if not args.jdbc_jar.is_file():raise ValueError('Official JDBC JAR missing')
        platform=env_file(args.platform_env);source=env_file(args.source_env)
        password=platform['ZETARIS_ADMIN_PASSWORD'];reader=source['TRAVEL_READER_PASSWORD'];secrets=[password,reader]
        # Port stays local even if a supplied env contains unrelated server settings.
        port=platform.get('DRIVER_PORT','10000')
        if not re.fullmatch(r'[0-9]{1,5}',port) or not 1<=int(port)<=65535:raise ValueError('Invalid local JDBC port')
        connection=jaydebeapi.connect('com.zetaris.lightning.jdbc.LightningDriver','jdbc:zetaris:lightning@localhost:'+port,[platform.get('ZETARIS_ADMIN_EMAIL','admin@zetaris.com'),password],str(args.jdbc_jar.resolve()))
        try:
            cursor=connection.cursor()
            def query(sql):
                cursor.execute(sql)
                return cursor.fetchall() if cursor.description else []
            one=query('SELECT 1 AS one')
            if str(one[0][0])!='1':raise ValueError('Zetaris query engine did not return 1')
            sources=query('SHOW DATASOURCES')
            known=any(str(cell)=='sydneybuddy_travel' for row in sources for cell in row)
            if args.register and not known:
                query('CREATE DATASOURCE sydneybuddy_travel DESCRIBE BY "SydneyBuddy public local travel data" OPTIONS (jdbcdriver "org.postgresql.Driver", jdbcurl "jdbc:postgresql://host.docker.internal:55433/travel_public", username "travel_reader", password '+literal(reader)+', fetchsize "1000")')
            if args.register:
                # Select before creating, to preserve any previously registered table.
                for table in TABLES.values():
                    try:query('SELECT COUNT(*) FROM sydneybuddy_travel.'+table)
                    except Exception:query('REGISTER DATASOURCE TABLE "'+table+'" FROM sydneybuddy_travel')
            if args.cache:
                for table in TABLES.values():query('CACHE TABLE sydneybuddy_travel.'+table)
            counts={kind:int(query('SELECT COUNT(*) FROM sydneybuddy_travel.'+table)[0][0]) for kind,table in TABLES.items()}
            expected=json.loads(args.manifest.read_text())['counts']
            if any(counts[k]!=expected.get(k,0) for k in TABLES):raise ValueError('Zetaris/source counts differ')
            with args.records.open(newline='') as stream:
                expected_cafes=sum(r['dataset']=='poi' and r['category']=='food' and 'cafe' in r['name'].lower() for r in csv.DictReader(stream))
            cafe_count=int(query("SELECT COUNT(*) FROM sydneybuddy_travel.travel_poi WHERE category='food' AND LOWER(name) LIKE '%cafe%'")[0][0])
            cafes=query("SELECT id,name,category FROM sydneybuddy_travel.travel_poi WHERE category = 'food' AND LOWER(name) LIKE '%cafe%' LIMIT 5")
            if cafe_count!=expected_cafes or len(cafes)!=min(5,expected_cafes):raise ValueError('Filtered COUNT/LIMIT mismatch. This tested Zetaris release needs --cache for these flat JDBC views.')
            result={'status':'verified','realZetaris':True,'protocol':'official JDBC','checkedAt':datetime.now(timezone.utc).isoformat(),'counts':counts,'filteredCafeCount':cafe_count,'cacheRequested':args.cache,'sampleCafes':[[str(c) for c in row] for row in cafes]}
            if args.output:args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
            print(json.dumps(result,ensure_ascii=False));cursor.close()
        finally:connection.close()
        return 0
    except Exception as error:
        # Database diagnostics may include SQL. Never print CREATE statements containing passwords.
        message=str(error)
        for secret in secrets:
            if secret:message=message.replace(secret,'[redacted]').replace(literal(secret),'[redacted]')
        if 'password' in message.lower() or 'CREATE DATASOURCE' in message.upper():message='Connection or registration failed; credentials and SQL are hidden.'
        print(json.dumps({'status':'failed','realZetaris':False,'error':message[:1500]},ensure_ascii=False),file=sys.stderr)
        return 1
if __name__=='__main__':raise SystemExit(main())
