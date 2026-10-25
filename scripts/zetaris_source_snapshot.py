#!/usr/bin/env python3
"""Export the live public source DB, or restore its reviewed JSON snapshot into an EMPTY DB.
Never exports platform credentials, metastore, users, raw posts or cached weather.
"""
import argparse, base64, hashlib, json, os, re, subprocess, sys, zipfile
from datetime import datetime, timezone
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
TABLES={'travel_records':('dataset','record_id','name','category','lat','lng','source','observed_at','payload'),
        **{name:('id','payload') for name in ('hidden_gem_records','osm_map_records','social_place_records','restaurant_place_records','activity_offer_records')}}
TABLES['hidden_gem_records']=TABLES['osm_map_records']=('id','name','lat','lng','payload')
VIEWS={'travel_poi':"SELECT record_id AS id,name,category,lat,lng,source,observed_at,payload FROM travel_records WHERE dataset='poi'",
       'travel_catalog':"SELECT record_id AS id,name,category,lat,lng,source,observed_at,payload FROM travel_records WHERE dataset='catalog'",
       'travel_map_layers':"SELECT record_id AS id,name,source,payload FROM travel_records WHERE dataset='map_layers'",
       'travel_metadata':"SELECT record_id AS id,source,observed_at,payload FROM travel_records WHERE dataset='metadata'",
       **{v:f'SELECT '+(','.join(TABLES[t]))+f' FROM {t}' for v,t in zip(('travel_hidden_gems','travel_osm_places','travel_social_places','travel_restaurant_places','travel_activity_offers'),list(TABLES)[1:])}}
MAX_BYTES=100*1024*1024

def check(doc):
    if doc.get('schemaVersion')!=1 or set(doc.get('tables',{}))!=set(TABLES):raise ValueError('Unexpected snapshot tables')
    from package_zetaris_migration import public_payload
    for table,columns in TABLES.items():
        rows=doc['tables'][table]
        if not isinstance(rows,list) or len(rows)>100000:raise ValueError('Invalid row collection')
        seen=set()
        for row in rows:
            if not isinstance(row,dict) or set(row)!=set(columns):raise ValueError('Unexpected columns')
            identity=(row.get('dataset'),row.get('record_id')) if table=='travel_records' else row.get('id')
            if identity in seen:raise ValueError('Duplicate record')
            seen.add(identity)
            if table=='travel_records' and row['dataset'] not in ('poi','catalog','map_layers','metadata'):raise ValueError('Weather snapshots are excluded')
            payload=json.loads(row['payload'])
            if not isinstance(payload,dict):raise ValueError('Invalid payload')
            public_payload(row)
            public_payload(payload)
    return doc

def read_snapshot(path):
    with zipfile.ZipFile(path) as z:
        if set(z.namelist())!={'snapshot.json','manifest.json'} or len(z.infolist())!=2 or sum(i.file_size for i in z.infolist())>MAX_BYTES:raise ValueError('Invalid archive')
        raw=z.read('snapshot.json');manifest=json.loads(z.read('manifest.json'))
    if hashlib.sha256(raw).hexdigest()!=manifest.get('sha256'):raise ValueError('Checksum mismatch')
    doc=check(json.loads(raw))
    if manifest.get('counts')!={k:len(v) for k,v in doc['tables'].items()}:raise ValueError('Count mismatch')
    return doc,manifest

def restore_sql(doc,reader=None):
    check(doc)
    lines=['SET search_path TO public;']
    for table,columns in TABLES.items():
        if table=='travel_records':
            schema="dataset text NOT NULL CHECK(dataset IN ('poi','catalog','map_layers','metadata')),record_id text NOT NULL,name text NOT NULL,category text NOT NULL,lat double precision CHECK(lat BETWEEN -90 AND 90),lng double precision CHECK(lng BETWEEN -180 AND 180),source text NOT NULL,observed_at timestamptz NOT NULL,payload text NOT NULL CHECK(jsonb_typeof(payload::jsonb)='object'),PRIMARY KEY(dataset,record_id)"
        else:schema="id text PRIMARY KEY,"+("name text NOT NULL,lat double precision CHECK(lat BETWEEN -90 AND 90),lng double precision CHECK(lng BETWEEN -180 AND 180)," if len(columns)>2 else "")+"payload text NOT NULL CHECK(jsonb_typeof(payload::jsonb)='object')"
        lines.append(f'CREATE TABLE public.{table} ({schema});')
        lines.append('CREATE TEMP TABLE snapshot_batch(encoded text) ON COMMIT DROP;')
        lines.append('COPY snapshot_batch(encoded) FROM STDIN;')
        # Base64 cannot inject SQL or a COPY end-marker, even when names/quotes contain newlines.
        lines.extend(base64.b64encode(json.dumps(row,ensure_ascii=False).encode()).decode() for row in doc['tables'][table])
        lines.append('\\.')
        lines.append(f"INSERT INTO public.{table} SELECT (jsonb_populate_record(NULL::public.{table},convert_from(decode(encoded,'base64'),'UTF8')::jsonb)).* FROM snapshot_batch;")
        lines.append(f"DO $$ BEGIN IF (SELECT count(*) FROM public.{table}) <> {len(doc['tables'][table])} THEN RAISE EXCEPTION 'Snapshot row count mismatch'; END IF; END $$;")
        lines.append('DROP TABLE snapshot_batch;')
    for view,query in VIEWS.items():lines.append(f'CREATE VIEW public.{view} AS {query};')
    if reader:
        if not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]{0,62}',reader):raise ValueError('Invalid reader role')
        lines.append(f'GRANT USAGE ON SCHEMA public TO "{reader}";')
        lines.append('GRANT SELECT ON '+','.join('public.'+v for v in VIEWS)+f' TO "{reader}";')
    return '\n'.join(lines)+'\n'

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    action=parser.add_mutually_exclusive_group(required=True)
    action.add_argument('--export',type=Path,metavar='ZIP')
    action.add_argument('--restore',type=Path,metavar='ZIP')
    parser.add_argument('--source-project',type=Path,default=ROOT)
    parser.add_argument('--source-env',type=Path)
    parser.add_argument('--database')
    parser.add_argument('--reader-role')
    parser.add_argument('--execute',action='store_true')
    args=parser.parse_args()
    if args.export:
        env=args.source_env or args.source_project/'infra/zetaris/.env.local'
        command=['docker','compose','--env-file',str(env),'-f',str(args.source_project/'infra/zetaris/compose.source.yaml'),'exec','-T','source-db','psql','-X','-U','travel_source_admin','-d','travel_public','-At','-v','ON_ERROR_STOP=1']
        def query(sql):return subprocess.check_output(command+['-c',sql],text=True,stderr=subprocess.PIPE)
        actual=set(query("SELECT tablename FROM pg_tables WHERE schemaname='public'").splitlines())
        if actual!=set(TABLES):raise ValueError('Source schema changed; review and extend the explicit export tables first')
        # A single repeatable-read transaction provides one coherent live snapshot.
        selects=[]
        for table in TABLES:
            where=" WHERE dataset <> 'weather'" if table=='travel_records' else ''
            selects.append(f"SELECT json_build_object('table','{table}','row',row_to_json(t))::text FROM public.{table} t{where};")
        raw=query('BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;\n'+'\n'.join(selects)+'\nCOMMIT;')
        tables={k:[] for k in TABLES}
        for line in raw.splitlines():
            if line in ('BEGIN','COMMIT') or not line:continue
            row=json.loads(line);tables[row['table']].append(row['row'])
        doc=check({'schemaVersion':1,'tables':tables});data=json.dumps(doc,ensure_ascii=False).encode()
        if len(data)>MAX_BYTES:raise ValueError('Snapshot too large')
        manifest={'schemaVersion':1,'createdAt':datetime.now(timezone.utc).isoformat(),'region':'ap-southeast-2','kind':'public-source-db-live','weather':'live API only; excluded','sha256':hashlib.sha256(data).hexdigest(),'counts':{k:len(v) for k,v in tables.items()}}
        args.export.parent.mkdir(parents=True,exist_ok=True)
        with zipfile.ZipFile(args.export,'x',zipfile.ZIP_DEFLATED) as z:z.writestr('snapshot.json',data);z.writestr('manifest.json',json.dumps(manifest,indent=2))
        os.chmod(args.export,0o600)
        read_snapshot(args.export)
        checksum=args.export.with_suffix(args.export.suffix+'.sha256');checksum.write_text(hashlib.sha256(args.export.read_bytes()).hexdigest()+'  '+args.export.name+'\n')
        print(json.dumps({'status':'verified','path':str(args.export),'counts':manifest['counts'],'weatherExported':False}))
    else:
        doc,manifest=read_snapshot(args.restore)
        sql=restore_sql(doc,args.reader_role)
        if not args.execute:print(json.dumps({'status':'verified','counts':manifest['counts'],'databaseChanged':False}));return
        if not args.database or not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]{0,62}',args.database):raise ValueError('Explicit database name required')
        if os.environ.get('PGSSLMODE')!='verify-full':raise ValueError('Set PGSSLMODE=verify-full for cloud restore')
        cmd=['psql','-X','--dbname',args.database,'-v','ON_ERROR_STOP=1']
        result=subprocess.check_output(cmd+['-At','-c',"SELECT current_database(); SELECT count(*) FROM pg_class c JOIN pg_namespace n ON c.relnamespace=n.oid WHERE n.nspname='public' AND c.relkind IN ('r','v','m','S','f','p');"],text=True,stderr=subprocess.PIPE).splitlines()
        if result!=[args.database,'0']:raise ValueError('Target must be the explicitly named EMPTY database')
        subprocess.run(cmd+['--single-transaction','-f','-'],input=sql,text=True,check=True,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
        print('Snapshot restored; register the nine static views in cloud Zetaris. Weather must be registered separately without caching.')
if __name__=='__main__':
    try:main()
    except (ValueError,KeyError,OSError,subprocess.CalledProcessError,zipfile.BadZipFile):
        print('Snapshot operation failed; no credentials or source rows are printed. Check source availability, format, target permissions and TLS.',file=sys.stderr);sys.exit(1)
