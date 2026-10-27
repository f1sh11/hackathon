#!/usr/bin/env python3
"""Run inside the approved AWS CloudShell session. Private payloads are never logged."""
import argparse,copy,hashlib,io,json,os,subprocess,tarfile,time
from decimal import Decimal
from pathlib import Path
os.umask(0o077)
NS='zetaris';POD='sydneybuddy-0';NODE='ip-10-0-11-101.ap-southeast-2.compute.internal'
ROOT=Path(__file__).resolve().parent

def command(args,body=None,timeout=90):
    result=subprocess.run(args,input=body,capture_output=True,timeout=timeout)
    if result.returncode:
        (ROOT/'command-error.private.log').write_bytes(result.stderr)
        raise RuntimeError('Operation failed: '+args[0]+' '+args[1]+'; private diagnostics saved locally')
    return result.stdout

def kube(*args,body=None,timeout=90):return command(['kubectl',*args],body,timeout)
def read(*args):return json.loads(kube('get',*args,'-o','json','--request-timeout=20s'))
def qty(value):
    for suffix,multiplier in [('Ki',1024),('Mi',1024**2),('Gi',1024**3),('Ti',1024**4),('n',Decimal('0.000000001')),('u',Decimal('0.000001')),('m',Decimal('.001')),('k',1000),('M',1000000),('G',1000000000)]:
        if value.endswith(suffix):return Decimal(value[:-len(suffix)])*multiplier
    return Decimal(value)
def needs(pod,key):
    running=sum((qty(c.get('resources',{}).get('requests',{}).get(key,'0')) for c in pod['spec']['containers']),Decimal(0))
    sidecars=Decimal(0);peak=running
    for c in pod['spec'].get('initContainers',[]):
        value=qty(c.get('resources',{}).get('requests',{}).get(key,'0'))
        if c.get('restartPolicy')=='Always':sidecars+=value;peak=max(peak,sidecars)
        else:peak=max(peak,sidecars+value)
    return max(peak,running+sidecars)+qty(pod['spec'].get('overhead',{}).get(key,'0'))
def preflight(release):
    nodes=read('nodes');target=next(n for n in nodes['items'] if n['metadata']['name']==NODE)
    assert not target['spec'].get('taints') and not target['spec'].get('unschedulable')
    assert any(c['type']=='Ready' and c['status']=='True' for c in target['status']['conditions'])
    pods=[p for p in read('pods','-A')['items'] if p['spec'].get('nodeName')==NODE and p['status'].get('phase') not in ['Succeeded','Failed']]
    assert not any(p['metadata']['name']==POD for p in pods),'Trial already exists; do not rerun restore'
    workload=next(o for o in release['items'] if o['kind']=='StatefulSet')
    assert workload['spec']['replicas']==1
    assert workload['spec']['template']['spec']['nodeSelector']=={'kubernetes.io/hostname':NODE}
    template={'spec':workload['spec']['template']['spec']}
    for key in ['cpu','memory']:
        available=qty(target['status']['allocatable'][key])-sum((needs(p,key) for p in pods),Decimal(0))
        assert available>=needs(template,key),'Insufficient existing node capacity; do not scale'
    assert len(pods)+1<=int(target['status']['allocatable']['pods'])
    assert sum(int(c['spec']['resources']['requests']['storage'][:-2]) for c in workload['spec']['volumeClaimTemplates'])==7
    assert all('@sha256:' in c['image'] for c in template['spec']['containers'])
    assert next(o for o in release['items'] if o['kind']=='Service')['spec']['type']=='ClusterIP'
    (ROOT/'nodes.before.json').write_text(json.dumps([n['metadata']['uid'] for n in nodes['items']]))
    for obj in release['items']:
        if obj['kind']=='StorageClass':continue
        r=json.loads(kube('get',obj['kind'],obj['metadata']['name'],'-n',NS,'-o','json','--ignore-not-found') or b'null')
        assert r is None,'Resource name is already in use; abort instead of overwriting'
    ingress=read('ingress','zetaris-ndp-api','-n',NS)
    assert ingress['spec']['rules'][0]['host']=='api.a6c43f1d-2765-487d-89af-99b612b39de9.cloud.zetaris.com'
    (ROOT/'ingress.before.json').write_text(json.dumps(ingress))
    print('Existing node capacity and clean deployment names confirmed.',flush=True)

def payload():
    archive=ROOT/'aws-migration.private.tar.gz'
    assert hashlib.sha256(archive.read_bytes()).hexdigest()==(ROOT/'payload.sha256').read_text().strip()
    with tarfile.open(archive,'r:gz') as t:
        assert set(t.getnames())=={'env.private.json','secrets.private.json','postgres.dump','state.tar.gz'}
        return {n:t.extractfile(n).read() for n in t.getnames()}

def table_counts():
    sql="""SELECT json_object_agg(name,total) FROM (SELECT tablename AS name,(xpath('/row/c/text()',query_to_xml(format('SELECT count(*) AS c FROM %I.%I',schemaname,tablename),false,true,'')))[1]::text::bigint AS total FROM pg_tables WHERE schemaname='public') q;"""
    raw=kube('exec','-n',NS,POD,'-c','db','--','psql','-U','sydneybuddy','-d','sydneybuddy','-t','-A','-c',sql)
    return json.loads(raw)

def restore(release):
    assert not (ROOT/'restored.json').exists(),'Restore already completed'
    preflight(release);p=payload();env=json.loads(p['env.private.json'])
    for name,data in [('sydneybuddy-runtime-env',env),('sydneybuddy-runtime-files',{'.secrets.json':p['secrets.private.json'].decode()})]:
        obj={'apiVersion':'v1','kind':'Secret','metadata':{'name':name,'namespace':NS,'labels':{'app.kubernetes.io/name':'sydneybuddy'}},'type':'Opaque','stringData':data}
        kube('create','-f','-',body=json.dumps(obj).encode())
    staging=copy.deepcopy(release)
    api=next(c for o in staging['items'] if o['kind']=='StatefulSet' for c in o['spec']['template']['spec']['containers'] if c['name']=='api')
    api['command']=['/bin/sh','-c','sleep infinity']
    for field in ['startupProbe','readinessProbe','livenessProbe']:api.pop(field,None)
    kube('apply','-f','-',body=json.dumps(staging).encode())
    print('Private restore pod and encrypted 7 GiB storage requested on the existing node.',flush=True)
    kube('wait','--for=condition=Ready','pod/'+POD,'-n',NS,'--timeout=240s',timeout=260)
    current=read('pod',POD,'-n',NS);assert current['spec']['nodeName']==NODE
    kube('exec','-i','-n',NS,POD,'-c','db','--','pg_restore','-U','sydneybuddy','-d','sydneybuddy','--single-transaction','--no-owner','--no-privileges',body=p['postgres.dump'],timeout=180)
    with tarfile.open(fileobj=io.BytesIO(p['state.tar.gz']),mode='r:gz') as t:
        for m in t.getmembers():assert not Path(m.name).is_absolute() and '..' not in Path(m.name).parts and not m.issym() and not m.islnk() and Path(m.name).name!='.secrets.json'
    kube('exec','-i','-n',NS,POD,'-c','api','--','tar','-xzf','-','-C','/var/lib/sydneybuddy','--no-same-owner',body=p['state.tar.gz'])
    counts=table_counts();expected=json.loads((ROOT/'expected-row-counts.json').read_text());assert counts==expected,'Restore row counts differ'
    (ROOT/'restored.json').write_text(json.dumps({'counts':counts,'node':NODE}))
    print('Database row counts and application-state restore verified; public route still unchanged.',flush=True)

def start(release):
    assert (ROOT/'restored.json').exists()
    kube('apply','-f','-',body=json.dumps(release).encode())
    kube('delete','pod',POD,'-n',NS,'--wait=true','--timeout=90s',timeout=100)
    kube('wait','--for=condition=Ready','pod/'+POD,'-n',NS,'--timeout=240s',timeout=260)
    assert read('pod',POD,'-n',NS)['spec']['nodeName']==NODE
    js="const h=await (await fetch('http://127.0.0.1:8787/health')).json();const s=await (await fetch('http://127.0.0.1:8787/planning/agent/status')).json();if(h.status!=='ok'||!h.nvidiaConfigured||!h.nswTransitConfigured||h.managedTravelDataSource!=='zetaris-cloud'||s.skillVersion!=='2026-10-27.3')process.exit(1);console.log(JSON.stringify({status:h.status,cloudZetaris:h.managedTravelDataSource,nvidia:h.nvidiaConfigured,transit:h.nswTransitConfigured,skill:s.skillVersion}));"
    print(kube('exec','-n',NS,POD,'-c','api','--','node','--input-type=module','-e',js).decode().strip(),flush=True)
    (ROOT/'started.ok').write_text('ok')

def publish():
    assert (ROOT/'started.ok').exists()
    before=read('ingress','zetaris-ndp-api','-n',NS);saved=json.loads((ROOT/'ingress.before.json').read_text())
    assert before['spec']==saved['spec'],'Ingress changed since review'
    path={'path':'/sydneybuddy/','pathType':'Prefix','backend':{'service':{'name':'sydneybuddy','port':{'number':8080}}}}
    patch=[{'op':'test','path':'/metadata/resourceVersion','value':before['metadata']['resourceVersion']},{'op':'add','path':'/spec/rules/0/http/paths/0','value':path}]
    kube('patch','ingress','zetaris-ndp-api','-n',NS,'--type=json','--patch',json.dumps(patch))
    after=read('ingress','zetaris-ndp-api','-n',NS);paths=after['spec']['rules'][0]['http']['paths'];assert paths[0]==path and paths[1:]==saved['spec']['rules'][0]['http']['paths']
    old=json.loads((ROOT/'nodes.before.json').read_text());now=[n['metadata']['uid'] for n in read('nodes')['items']];assert sorted(old)==sorted(now),'Existing nodes changed; investigate before cutover'
    (ROOT/'published.ok').write_text('ok')
    print('One path added to the existing ingress; existing routes preserved and node set unchanged.',flush=True)

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('phase',choices=['restore','start','publish']);a=parser.parse_args()
    context=kube('config','current-context').decode().strip()
    if context!='arn:aws:eks:ap-southeast-2:560570264527:cluster/zetaris-sydneybuddy-demo-a6c43f1d': raise RuntimeError('Refusing a different Kubernetes context')
    release=json.loads((ROOT/'colocation-release.json').read_text())
    if a.phase=='restore':restore(release)
    elif a.phase=='start':start(release)
    else:publish()
if __name__=='__main__':main()
