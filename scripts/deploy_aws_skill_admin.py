#!/usr/bin/env python3
"""Deploy the Skill admin API to the existing single-node workload, without Secret reads."""
import argparse,json,re,subprocess
from pathlib import Path
NS='zetaris';NAME='sydneybuddy';POD='sydneybuddy-0'
CONTEXT='arn:aws:eks:ap-southeast-2:560570264527:cluster/zetaris-sydneybuddy-demo-a6c43f1d'
PREFIX='560570264527.dkr.ecr.ap-southeast-2.amazonaws.com/sydneybuddy-api@sha256:'
NODE='ip-10-0-11-101.ap-southeast-2.compute.internal'
TOKEN_PATH='/var/lib/sydneybuddy/planning-skills/admin-token'
def kube(*args,data=None):
    r=subprocess.run(['kubectl',*args],input=data,capture_output=True,timeout=360)
    if r.returncode:raise RuntimeError(r.stderr.decode()[:1200])
    return r.stdout

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('image');p.add_argument('--token-file',type=Path,required=True);p.add_argument('--record',type=Path,default=Path('skill-admin-deployment.private.json'));a=p.parse_args()
    assert re.fullmatch(re.escape(PREFIX)+r'[a-f0-9]{64}',a.image),'Pinned ECR API image required'
    token=a.token_file.read_bytes();assert re.fullmatch(rb'[A-Za-z0-9_-]{64}',token),'Expected generated 48-byte credential'
    assert kube('config','current-context').decode().strip()==CONTEXT,'Wrong cluster'
    read=lambda kind:json.loads(kube('get',kind,NAME if kind=='statefulset' else POD,'-n',NS,'-o','json'))
    before=read('statefulset');pod=read('pod');spec=before['spec'];template=spec['template']['spec']
    assert spec['replicas']==1 and spec['updateStrategy']['type']=='OnDelete'
    assert template['nodeSelector']=={'kubernetes.io/hostname':NODE} and pod['spec']['nodeName']==NODE
    index=next(i for i,c in enumerate(template['containers']) if c['name']=='api');old=template['containers'][index]
    assert old['image'].startswith(PREFIX) and old['image']!=a.image
    # An existing credential can be reused only if it equals this exact approved value. Never rotate silently.
    write="""import fs from 'node:fs';import crypto from 'node:crypto';const p='/var/lib/sydneybuddy/planning-skills/admin-token';let b=Buffer.alloc(0);for await(const c of process.stdin)b=Buffer.concat([b,c]);if(!/^[A-Za-z0-9_-]{64}$/.test(b.toString()))throw Error('Invalid credential');fs.mkdirSync('/var/lib/sydneybuddy/planning-skills',{recursive:true});if(fs.existsSync(p)){const h=x=>crypto.createHash('sha256').update(x).digest();if(!crypto.timingSafeEqual(h(fs.readFileSync(p)),h(b)))throw Error('Existing credential differs; refusing rotation');}else fs.writeFileSync(p,b,{mode:0o600,flag:'wx'});console.log('Private admin credential ready');"""
    kube('exec','-i','-n',NS,POD,'-c','api','--','node','--input-type=module','-e',write,data=token)
    updated=json.loads(json.dumps(old));updated['image']=a.image
    env=updated.setdefault('env',[]);env=[e for e in env if e['name'] not in ('SKILL_ADMIN_TOKEN','SKILL_ADMIN_TOKEN_FILE')];env.append({'name':'SKILL_ADMIN_TOKEN_FILE','value':TOKEN_PATH});updated['env']=env
    a.record.write_text(json.dumps({'before':before,'oldPodUid':pod['metadata']['uid'],'newImage':a.image},indent=2));a.record.chmod(0o600)
    location=f'/spec/template/spec/containers/{index}'
    kube('patch','statefulset',NAME,'-n',NS,'--type=json','-p',json.dumps([{'op':'test','path':'/metadata/resourceVersion','value':before['metadata']['resourceVersion']},{'op':'replace','path':location,'value':updated}]))
    try:
        kube('delete','pod',POD,'-n',NS,'--wait=true','--timeout=120s');kube('wait','--for=condition=Ready','pod/'+POD,'-n',NS,'--timeout=300s')
        current=read('pod');assert current['spec']['nodeName']==NODE and current['metadata']['uid']!=pod['metadata']['uid']
        assert next(c['image'] for c in current['spec']['containers'] if c['name']=='api')==a.image
        check="""import fs from 'node:fs';const base='http://127.0.0.1:8787';const h=await(await fetch(base+'/health')).json();if(h.status!=='ok'||h.managedTravelDataSource!=='zetaris-cloud'||!h.nvidiaConfigured||!h.nswTransitConfigured)throw Error('Backend health failed');const denied=await fetch(base+'/admin/planning/skills/status');if(denied.status!==401)throw Error('Admin auth bypass');const r=await fetch(base+'/admin/planning/skills/status',{headers:{'X-Skill-Admin-Token':fs.readFileSync('/var/lib/sydneybuddy/planning-skills/admin-token','utf8')}});if(!r.ok)throw Error('Admin not ready');const s=await r.json();console.log(JSON.stringify({status:h.status,dataSource:h.managedTravelDataSource,adminConfigured:true,skillVersion:s.activeVersion,activeValid:s.activeValid}));"""
        print(kube('exec','-n',NS,POD,'-c','api','--','node','--input-type=module','-e',check).decode().strip())
        after=read('statefulset');expected=json.loads(json.dumps(before['spec']));expected['template']['spec']['containers'][index]=updated
        assert after['spec']==expected,'Unexpected workload change'
        print('Existing node, volumes, database, gateway and ingress preserved.')
    except Exception:
        latest=read('statefulset');kube('patch','statefulset',NAME,'-n',NS,'--type=json','-p',json.dumps([{'op':'test','path':'/metadata/resourceVersion','value':latest['metadata']['resourceVersion']},{'op':'replace','path':location,'value':old}]))
        kube('delete','pod',POD,'-n',NS,'--wait=true','--timeout=120s');kube('wait','--for=condition=Ready','pod/'+POD,'-n',NS,'--timeout=300s')
        print('Deployment failed; prior API container configuration restored.');raise
if __name__=='__main__':main()
