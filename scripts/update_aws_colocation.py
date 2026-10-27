#!/usr/bin/env python3
"""Update only the existing co-located API image; preserve data, gateway and routing."""
import argparse,json,re,subprocess,time
from pathlib import Path
NS='zetaris'; NAME='sydneybuddy'; POD=NAME+'-0'
CONTEXT='arn:aws:eks:ap-southeast-2:560570264527:cluster/zetaris-sydneybuddy-demo-a6c43f1d'
NODE='ip-10-0-11-101.ap-southeast-2.compute.internal'
PREFIX='560570264527.dkr.ecr.ap-southeast-2.amazonaws.com/sydneybuddy-api@sha256:'
def run(*args):
    r=subprocess.run(['kubectl',*args],capture_output=True,text=True,timeout=360)
    if r.returncode: raise RuntimeError(r.stderr[:1500])
    return r.stdout

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('image');p.add_argument('--record',default='skill-update-20261027-5.json');a=p.parse_args()
    assert re.fullmatch(re.escape(PREFIX)+r'[a-f0-9]{64}',a.image),'Pinned app repository image required'
    assert run('config','current-context').strip()==CONTEXT,'Wrong cluster'
    read=lambda kind,name:json.loads(run('get',kind,name,'-n',NS,'-o','json'))
    before=read('statefulset',NAME);spec=before['spec'];template=spec['template']['spec']
    assert spec['replicas']==1 and spec['updateStrategy']['type']=='OnDelete'
    assert template['nodeSelector']=={'kubernetes.io/hostname':NODE}
    pod=read('pod',POD);assert pod['spec']['nodeName']==NODE
    index=next(i for i,c in enumerate(template['containers']) if c['name']=='api')
    old=template['containers'][index]['image'];assert old.startswith(PREFIX)
    assert old!=a.image,'Already updated'
    backup={'oldImage':old,'newImage':a.image,'statefulset':before,'podUid':pod['metadata']['uid']}
    Path(a.record).write_text(json.dumps(backup,indent=2))
    patch=[{'op':'test','path':'/metadata/resourceVersion','value':before['metadata']['resourceVersion']},
           {'op':'test','path':f'/spec/template/spec/containers/{index}/image','value':old},
           {'op':'replace','path':f'/spec/template/spec/containers/{index}/image','value':a.image}]
    run('patch','statefulset',NAME,'-n',NS,'--type=json','-p',json.dumps(patch))
    try:
        run('delete','pod',POD,'-n',NS,'--wait=true','--timeout=120s')
        run('wait','--for=condition=Ready','pod/'+POD,'-n',NS,'--timeout=300s')
        current=read('pod',POD)
        assert current['spec']['nodeName']==NODE and current['metadata']['uid']!=pod['metadata']['uid']
        assert next(c['image'] for c in current['spec']['containers'] if c['name']=='api')==a.image
        js="const h=await(await fetch('http://127.0.0.1:8787/health')).json();const s=await(await fetch('http://127.0.0.1:8787/planning/agent/status')).json();if(h.status!=='ok'||h.managedTravelDataSource!=='zetaris-cloud'||!h.nvidiaConfigured||!h.nswTransitConfigured||s.skillVersion!=='2026-10-27.5')throw Error('Health/skill check failed');console.log(JSON.stringify({status:h.status,source:h.managedTravelDataSource,skill:s.skillVersion}));"
        result=run('exec','-n',NS,POD,'-c','api','--','node','--input-type=module','-e',js)
        after=read('statefulset',NAME);expected=json.loads(json.dumps(before['spec']));expected['template']['spec']['containers'][index]['image']=a.image
        assert after['spec']==expected,'Unexpected workload changes'
        print(result.strip());print('Existing node, data volumes, secrets, resources and routing retained.')
    except Exception:
        run('set','image','statefulset/'+NAME,'api='+old,'-n',NS)
        run('delete','pod',POD,'-n',NS,'--wait=true','--timeout=120s')
        run('wait','--for=condition=Ready','pod/'+POD,'-n',NS,'--timeout=300s')
        print('Update failed; previous API image restored.');raise
if __name__=='__main__':main()
