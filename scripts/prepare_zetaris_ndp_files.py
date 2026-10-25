#!/usr/bin/env python3
"""Prepare bounded JSON upload batches for Zetaris NDP File System. No upload or credentials."""
import argparse,hashlib,json,re
from pathlib import Path
from prepare_zetaris_cloud_files import grouped_rows
from zetaris_source_snapshot import read_snapshot

def materialize(snapshot,output,prefix,max_bytes=850_000):
    if not re.fullmatch(r'dffs://[A-Za-z0-9_-]+(?:/[A-Za-z0-9_-]+)*',prefix):
        raise ValueError('Explicit safe dffs:// target folder required')
    if not 4096<=max_bytes<=850_000:raise ValueError('Upload batch size must be between 4096 and 850000 bytes')
    doc,_=read_snapshot(snapshot);groups=grouped_rows(doc)
    # Preserve each JSON record intact. Never split a UTF-8 line or change its payload.
    parts=[]
    for table,rows in groups.items():
        chunk=bytearray();part=0
        for row in rows:
            line=(json.dumps(row,ensure_ascii=False)+'\n').encode('utf-8')
            if len(line)>max_bytes:raise ValueError('One record exceeds the upload batch limit')
            if chunk and len(chunk)+len(line)>max_bytes:
                parts.append((f'{table}-{part:04}.json',bytes(chunk)));chunk=bytearray();part+=1
            chunk.extend(line)
        if chunk:parts.append((f'{table}-{part:04}.json',bytes(chunk)))
    output.mkdir(parents=True,exist_ok=False)
    files=[];batch=1;batch_bytes=0
    for name,raw in parts:
        if batch_bytes and batch_bytes+len(raw)>max_bytes:batch+=1;batch_bytes=0
        path=output/f'batch-{batch:03}'/name;path.parent.mkdir(exist_ok=True);path.write_bytes(raw)
        batch_bytes+=len(raw)
        files.append({'file':str(path.relative_to(output)),'bytes':len(raw),'sha256':hashlib.sha256(raw).hexdigest()})
    statements=['-- Upload each batch separately into the SAME NDP target folder. Do not register before all batches finish.']
    for table,rows in groups.items():
        if rows:statements.append(f'CREATE LIGHTNING FILESTORE TABLE {table} FROM sydneybuddy_travel FORMAT JSON OPTIONS(path "{prefix}/{table}-*.json", inferSchema "true");')
    (output/'register-ndp.sql').write_text('\n'.join(statements)+'\n')
    manifest={'counts':{k:len(v) for k,v in groups.items()},'files':files,'batches':batch if files else 0,'target':prefix,'weather':'live API; never bundled'}
    (output/'manifest.json').write_text(json.dumps(manifest,indent=2))
    return manifest

if __name__=='__main__':
    cli=argparse.ArgumentParser(description=__doc__)
    cli.add_argument('--snapshot',type=Path,required=True);cli.add_argument('--output',type=Path,required=True)
    cli.add_argument('--ndp-prefix',required=True);args=cli.parse_args()
    result=materialize(args.snapshot,args.output,args.ndp_prefix)
    print(json.dumps({'batches':result['batches'],'files':len(result['files']),'counts':result['counts']}))
