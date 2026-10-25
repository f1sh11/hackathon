#!/usr/bin/env python3
"""Convert a verified live source snapshot into private S3 JSON-lines inputs.
No upload, credentials, database or cloud mutations. Nonempty tables only; empty collections are explicit in manifest.
"""
import argparse,json,re
from pathlib import Path
from urllib.parse import urlsplit
from zetaris_source_snapshot import read_snapshot

def materialize(snapshot,output,prefix):
    uri=urlsplit(prefix)
    if uri.scheme!='s3' or not re.fullmatch('[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]',uri.netloc) or uri.query or uri.fragment or not re.fullmatch('/[A-Za-z0-9/_-]+',uri.path):raise ValueError('Explicit safe private S3 prefix required')
    doc,_=read_snapshot(snapshot)
    output.mkdir(parents=True,exist_ok=False)
    tables=doc['tables'];groups={}
    for key,name in [('poi','travel_poi'),('catalog','travel_catalog'),('map_layers','travel_map_layers'),('metadata','travel_metadata')]:
        groups[name]=[{'id':r['record_id'],'payload':r['payload']} for r in tables['travel_records'] if r['dataset']==key]
    for table,view in [('hidden_gem_records','travel_hidden_gems'),('osm_map_records','travel_osm_places'),('social_place_records','travel_social_places'),('restaurant_place_records','travel_restaurant_places'),('activity_offer_records','travel_activity_offers')]:
        groups[view]=[{'id':r['id'],'payload':r['payload']} for r in tables[table]]
    statements=['CREATE LIGHTNING DATABASE sydneybuddy_travel DESCRIBE BY "SydneyBuddy reviewed public source snapshot";']
    for table,rows in groups.items():
        if not rows:continue
        (output/(table+'.json')).write_text(''.join(json.dumps(row,ensure_ascii=False)+'\n' for row in rows))
        statements.append('CREATE LIGHTNING FILESTORE TABLE '+table+' FROM sydneybuddy_travel FORMAT JSON OPTIONS(path "'+prefix.rstrip('/')+'/'+table+'.json", inferSchema "true");')
    (output/'register-s3.sql').write_text('-- Review target namespace and IAM access first. Execute one statement per request.\n'+'\n'.join(statements)+'\n')
    (output/'manifest.json').write_text(json.dumps({'region':'ap-southeast-2','counts':{k:len(v) for k,v in groups.items()},'emptyTables':[k for k,v in groups.items() if not v],'weather':'live API; never bundled','privateBucketRequired':True},indent=2))
    return {k:len(v) for k,v in groups.items()}
if __name__=='__main__':
    cli=argparse.ArgumentParser(description=__doc__);cli.add_argument('--snapshot',type=Path,required=True);cli.add_argument('--output',type=Path,required=True);cli.add_argument('--s3-prefix',required=True);a=cli.parse_args()
    print(json.dumps(materialize(a.snapshot,a.output,a.s3_prefix)))
