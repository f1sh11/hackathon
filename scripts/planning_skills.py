#!/usr/bin/env python3
"""Prepare append-only bilingual Skill updates; activate atomically through operator kubectl access."""
import argparse,hashlib,json,re,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
BASE='2026-10-27.5'
CONTEXT='arn:aws:eks:ap-southeast-2:560570264527:cluster/zetaris-sydneybuddy-demo-a6c43f1d'
FOLDER=ROOT/'agent-skills'

def build(folder):
    meta=json.loads((folder/'version.json').read_text());version=meta['version']
    if meta.get('baseVersion')!=BASE or version==BASE or not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._-]{0,79}',version):raise ValueError('Choose a new version and preserve baseVersion')
    result={'version':version,'baseVersion':BASE}
    for language,key in [('en','enAppendix'),('zh-Hant','zhHantAppendix')]:
        base=(ROOT/'server-dotnet/SydneyBuddy.Api/Planning/Skills'/f'itinerary.{language}.md').read_text()
        edited=(folder/f'itinerary.{language}.md').read_text()
        if not edited.startswith(base):raise ValueError(f'{language}: original rules changed or removed; append new rules at the end')
        appendix=edited[len(base):]
        if not appendix.strip() or len(appendix.encode())>64000:raise ValueError(f'{language}: append 1–64000 UTF-8 bytes')
        result[key]=appendix
    data=json.dumps(result,ensure_ascii=False,indent=2).encode()
    if len(data)>150000:raise ValueError('Bundle too large')
    return data

def kube(args,data=None):
    return subprocess.check_output(['kubectl',*args],input=data)

# Fixed private state location; no shell interpolation, Secret reads, or arbitrary filesystem paths.
INSTALL=r'''
import fs from 'node:fs';import crypto from 'node:crypto';
const dir='/var/lib/sydneybuddy/planning-skills';fs.mkdirSync(dir,{recursive:true});
const active=dir+'/active.json',action=process.argv[1],history=dir+'/history';fs.mkdirSync(history,{recursive:true});
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const previous=fs.existsSync(active)?fs.readFileSync(active):null;
let next=Buffer.alloc(0);
if(action==='rollback'){
 if(!previous)throw Error('No active override');
 const manifest=JSON.parse(fs.readFileSync(dir+'/previous.json','utf8'));
 if(manifest.embedded){if(previous&&!fs.existsSync(history+'/'+hash(previous)+'.json'))fs.writeFileSync(history+'/'+hash(previous)+'.json',previous,{mode:0o600,flag:'wx'});fs.unlinkSync(active);console.log('Restored embedded Skill');process.exit(0);}
 next=fs.readFileSync(history+'/'+manifest.hash+'.json');
}else{
 const chunks=[];for await(const c of process.stdin)chunks.push(c);next=Buffer.concat(chunks);
 if(next.length>150000)throw Error('Oversized bundle');
 const n=JSON.parse(next);if(n.baseVersion!=='2026-10-27.5'||!n.enAppendix?.trim()||!n.zhHantAppendix?.trim()||!/^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$/.test(n.version)||n.version===n.baseVersion||Buffer.byteLength(n.enAppendix)>64000||Buffer.byteLength(n.zhHantAppendix)>64000)throw Error('Invalid bundle');
 if(previous){const old=JSON.parse(previous);if(n.version===old.version||!n.enAppendix.startsWith(old.enAppendix)||!n.zhHantAppendix.startsWith(old.zhHantAppendix))throw Error('New update must retain existing appendices and use a new version');}
}
if(previous){const h=hash(previous),p=history+'/'+h+'.json';if(!fs.existsSync(p))fs.writeFileSync(p,previous,{mode:0o600,flag:'wx'});fs.writeFileSync(dir+'/previous.json',JSON.stringify({hash:h}));}
else fs.writeFileSync(dir+'/previous.json',JSON.stringify({embedded:true}));
const temp=active+'.'+crypto.randomUUID()+'.tmp';fs.writeFileSync(temp,next,{mode:0o600,flag:'wx'});fs.renameSync(temp,active);
console.log(JSON.stringify({version:JSON.parse(next).version,sha256:hash(next),active:true}));
'''

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('action',choices=['init','build','push','rollback']);parser.add_argument('--folder',type=Path,default=FOLDER);parser.add_argument('--confirm-rollback',action='store_true');parser.add_argument('--bundle',type=Path,help='Push a prebuilt bundle without a repository checkout');a=parser.parse_args()
    if a.action=='init':
        a.folder.mkdir(parents=True,exist_ok=True)
        for language in ['en','zh-Hant']:
            target=a.folder/f'itinerary.{language}.md'
            if target.exists():raise ValueError('Workspace exists; refusing to overwrite edits')
            target.write_text((ROOT/'server-dotnet/SydneyBuddy.Api/Planning/Skills'/target.name).read_text())
        (a.folder/'version.json').write_text(json.dumps({'version':'2026-10-27.6','baseVersion':BASE},indent=2)+'\n')
        print('Edit both Markdown files by appending rules, then run build.');return
    if a.action=='rollback':
        if not a.confirm_rollback:raise ValueError('Review the removed rules first, then pass --confirm-rollback')
        data=None
    else:
        if a.bundle:
            if a.action!='push':raise ValueError('--bundle is only for push')
            data=a.bundle.read_bytes();bundle=json.loads(data)
            if len(data)>150000 or bundle.get('baseVersion')!=BASE or bundle.get('version')==BASE or not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._-]{0,79}',bundle.get('version','')):raise ValueError('Invalid bundle')
            for key in ['enAppendix','zhHantAppendix']:
                if not isinstance(bundle.get(key),str) or not bundle[key].strip() or len(bundle[key].encode())>64000:raise ValueError('Invalid bilingual appendix')
        else:
            data=build(a.folder);(a.folder/'bundle.json').write_bytes(data)
        print('Validated bundle SHA256: '+hashlib.sha256(data).hexdigest())
        if a.action=='build':return
    if kube(['config','current-context']).decode().strip()!=CONTEXT:raise ValueError('Wrong AWS Kubernetes context')
    kube(['get','pod','sydneybuddy-0','-n','zetaris','-o','name'])
    out=kube(['exec','-i','-n','zetaris','sydneybuddy-0','-c','api','--','node','--input-type=module','-e',INSTALL,a.action],data)
    print(out.decode().strip())
    print('Next requests use the new snapshot; existing requests retain their prior snapshot.')
if __name__=='__main__':main()
