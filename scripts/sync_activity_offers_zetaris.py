#!/usr/bin/env python3
"""Load a reviewed activity reference collection into local PostgreSQL and real Zetaris.
Only factual reference fields, no cookies, raw pages, photos or private user data.
"""
import argparse
import csv
import io
import json
import math
from datetime import date
from pathlib import Path
import re
import subprocess
from urllib.parse import urlsplit
from urllib.request import Request, urlopen
import uuid

ROOT = Path(__file__).resolve().parents[1]
FIELDS = ('id','title','venueName','lat','lng','amount','currency','ticketType','conditions',
          'sourceName','sourceUrl','observedOn','reviewBy','license')


def validate(document):
    rows = document.get('offers')
    if document.get('schemaVersion') != 1 or not isinstance(rows,list) or len(rows)>1000:
        raise ValueError('Invalid offer collection')
    result=[];seen=set()
    for row in rows:
        value={key:row[key] for key in FIELDS}
        status=row.get('pricingStatus','unknown' if value['amount'] is None else 'free' if value['amount']==0 else 'known')
        if status not in ['known','free','unknown']:raise ValueError('Invalid pricing status')
        if status=='unknown' and value['amount'] is not None:raise ValueError('Unknown must have a null amount')
        value['pricingStatus']=status
        if value['id'] in seen:raise ValueError('Duplicate ID')
        seen.add(value['id'])
        for key,maximum in [('id',150),('title',300),('venueName',200),('ticketType',200),('conditions',2000),('sourceName',200),('license',1000)]:
            if not isinstance(value[key],str) or not 1<=len(value[key])<=maximum:raise ValueError('Invalid text')
        for key,low,high in [('lat',-90,90),('lng',-180,180),('amount',0,100000)]:
            number=value[key]
            if key=='amount' and status=='unknown' and number is None:continue
            if isinstance(number,bool) or not isinstance(number,(float,int)) or not math.isfinite(number) or not low<=number<=high:raise ValueError('Invalid number')
        if status=='free' and value['amount']!=0 or status=='known' and value['amount']<=0:raise ValueError('Inconsistent price status')
        url=urlsplit(value['sourceUrl'])
        if url.scheme!='https' or not url.hostname or url.username or url.password:raise ValueError('Invalid source URL')
        if not re.fullmatch('[A-Z]{3}',value['currency']):raise ValueError('Explicit currency required')
        observed=date.fromisoformat(value['observedOn']);review=date.fromisoformat(value['reviewBy'])
        if observed>date.today() or review<observed:raise ValueError('Invalid dates')
        for key in ['venueIds','venueAliases']:
            entries=row.get(key,[])
            if not isinstance(entries,list) or len(entries)>30 or any(not isinstance(item,str) or not 1<=len(item)<=250 for item in entries):raise ValueError('Invalid venue matching metadata')
            value[key]=entries
        value.update(priceType='reference_ticket_price',budgetEligible=False)
        result.append(value)
    return result


def main():
    cli=argparse.ArgumentParser(description=__doc__)
    cli.add_argument('--input',type=Path,default=ROOT/'shared/activity-offers.json')
    cli.add_argument('--source-env',type=Path,default=ROOT/'infra/zetaris/.env.local')
    cli.add_argument('--platform-env',type=Path,required=True)
    args=cli.parse_args();offers=validate(json.loads(args.input.read_text()))
    data=io.StringIO();writer=csv.writer(data,lineterminator="\n")
    for offer in offers:writer.writerow([offer['id'],json.dumps(offer,ensure_ascii=False)])
    sql='''BEGIN;
CREATE TABLE IF NOT EXISTS activity_offer_records(id text PRIMARY KEY,payload text NOT NULL CHECK(jsonb_typeof(payload::jsonb)='object'));
CREATE OR REPLACE VIEW travel_activity_offers AS SELECT id,payload FROM activity_offer_records;
GRANT SELECT ON travel_activity_offers TO travel_reader;
CREATE TEMP TABLE activity_batch(id text PRIMARY KEY,payload text) ON COMMIT DROP;
COPY activity_batch(id,payload) FROM STDIN WITH(FORMAT csv);
'''+data.getvalue()+'''\\.
DELETE FROM activity_offer_records;
INSERT INTO activity_offer_records SELECT * FROM activity_batch;
COMMIT;
'''
    subprocess.run(['docker','compose','--env-file',str(args.source_env),'-f',str(ROOT/'infra/zetaris/compose.source.yaml'),'exec','-T','source-db','psql','-U','travel_source_admin','-d','travel_public','-v','ON_ERROR_STOP=1'],input=sql,text=True,capture_output=True,check=True)
    env={}
    for line in args.platform_env.read_text().splitlines():
        key,sep,value=line.partition('=')
        if sep and not key.startswith('#'):env[key.strip()]=value.strip().strip('\"\'')
    def post(url,body,headers=None):
        request=Request(url,data=json.dumps(body).encode(),headers={'Content-Type':'application/json',**(headers or {})})
        with urlopen(request,timeout=60) as response:return json.load(response)
    auth=post('http://127.0.0.1:3000/api/auth/login',{'username':env['ZETARIS_ADMIN_EMAIL'],'password':env['ZETARIS_ADMIN_PASSWORD']})
    def query(statement):
        result=post('http://127.0.0.1:8888/api/v1.0/sql-editor/sqls/run',{'queryId':str(uuid.uuid4()),'sql':statement,'source':'SqlEditor','limit':2000},{'Authorization':'Bearer '+auth['idToken'],'X-Org-ID':str(auth['orgId']),'X-Request-ID':str(uuid.uuid4())})
        if not isinstance(result.get('data'),list):raise ValueError('Zetaris query failed')
        return result['data']
    tables=query('SHOW TABLES FROM sydneybuddy_travel')
    if not any(row[0].lower()=='sydneybuddy_travel.travel_activity_offers' for row in tables):
        query('REGISTER DATASOURCE TABLE "travel_activity_offers" FROM sydneybuddy_travel')
    query('UNCACHE TABLE sydneybuddy_travel.travel_activity_offers')
    actual=[json.loads(row[0]) for row in query('SELECT payload FROM sydneybuddy_travel.travel_activity_offers')]
    if sorted(actual,key=lambda x:x['id'])!=sorted(offers,key=lambda x:x['id']):raise ValueError('Zetaris payload verification failed')
    print(json.dumps({'status':'verified','activityOffers':len(actual),'realZetaris':True,'budgetEligible':False}))


if __name__=='__main__':main()
