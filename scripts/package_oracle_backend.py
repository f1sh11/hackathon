#!/usr/bin/env python3
"""Package backend source and optional verified public snapshot. Never includes local app DBs or credentials."""
import argparse,hashlib,io,json,subprocess,tarfile
from datetime import datetime,timezone
from pathlib import Path
from zetaris_source_snapshot import read_snapshot

ROOT=Path(__file__).resolve().parents[1]

def public_files(snapshot):
    doc,_=read_snapshot(snapshot);tables=doc['tables']
    records=tables['travel_records']
    metadata=[json.loads(r['payload']) for r in records if r['dataset']=='metadata']
    if len(metadata)!=1:raise ValueError('Exactly one public POI metadata row required')
    poi=metadata[0];poi['places']=[json.loads(r['payload']) for r in records if r['dataset']=='poi']
    datasets={'infra/poi/data/places.json':poi}
    for table,file in [('hidden_gem_records','hidden-gems.json'),('osm_map_records','osm-map-places.json')]:
        datasets['infra/zetaris/data/'+file]={'places':[json.loads(r['payload']) for r in tables[table]]}
    datasets['shared/activity-offers.json']={'offers':[json.loads(r['payload']) for r in tables['activity_offer_records']]}
    return {name:json.dumps(value,ensure_ascii=False).encode() for name,value in datasets.items()}

def package(output,snapshot=None):
    paths=subprocess.check_output(['git','ls-files','-z'],cwd=ROOT).decode().split('\0')
    def allowed(name):
        return (name.startswith('server-dotnet/SydneyBuddy.Api/') and name.endswith(('.cs','.csproj'))) or name in ('server/places.json','server/hmem_bridge.ts') or name.startswith(('server/vendor/hmem/','shared/'))
    selected={n:(ROOT/n).read_bytes() for n in paths if n and allowed(n)}
    for name in ['.dockerignore','infra/oracle/Dockerfile','infra/oracle/compose.yaml','infra/oracle/healthcheck.mjs','infra/oracle/cloud-init.yaml','infra/oracle/.env.example','infra/oracle/README.md','scripts/oracle_backend.py','scripts/oracle_micro.py','infra/oracle/install-micro.py']:
        selected[name]=(ROOT/name).read_bytes()
    if snapshot:selected.update(public_files(snapshot))
    if any(Path(n).name in ('.env','.secrets.json') or '.hmem/' in n or '.pet-sessions/' in n for n in selected):raise ValueError('Private path in package')
    manifest={'schemaVersion':1,'createdAt':datetime.now(timezone.utc).isoformat(),'files':{n:hashlib.sha256(raw).hexdigest() for n,raw in selected.items()},'privateUserDataIncluded':False,'weather':'live API only'}
    selected['oracle-release-manifest.json']=json.dumps(manifest,indent=2).encode()
    output.parent.mkdir(parents=True,exist_ok=True)
    with output.open('xb') as stream,tarfile.open(fileobj=stream,mode='w:gz') as archive:
        for name,raw in selected.items():
            info=tarfile.TarInfo(name);info.size=len(raw);info.mode=0o644;archive.addfile(info,io.BytesIO(raw))
    output.with_suffix(output.suffix+'.sha256').write_text(hashlib.sha256(output.read_bytes()).hexdigest()+'  '+output.name+'\n')
    return {'output':str(output),'files':len(selected),'bytes':output.stat().st_size,'privateUserDataIncluded':False}

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--snapshot',type=Path);parser.add_argument('--output',type=Path)
    args=parser.parse_args();target=args.output or ROOT/'infra/oracle/bundles'/('backend-'+datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')+'.tar.gz')
    print(json.dumps(package(target,args.snapshot)))
