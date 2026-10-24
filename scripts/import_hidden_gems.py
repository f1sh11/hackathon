#!/usr/bin/env python3
"""Import a bounded Sydney community-guide discovery collection, retaining provenance.
No ratings, reviews, transit instructions or popularity scores are invented.
Photos are stored as licensed metadata/URLs, not downloaded image binaries.
"""
import argparse,csv,hashlib,html,json,math,re,ssl,time
from datetime import datetime,timezone
from pathlib import Path
from urllib.parse import urlencode,quote,urlparse
from urllib.request import Request,urlopen
ROOT=Path(__file__).resolve().parents[1]
PAGES=['Sydney/Lower_North_Shore','Sydney/City_Centre','Sydney/Eastern_Suburbs']
# Editorial discovery list, not a measured claim about visitor numbers or legal public access.
NAMES={"Wendy Whiteley's Secret Garden",'Sawmillers Reserve','Balls Head Reserve','Berry Island Reserve','Cremorne Reserve','Blues Point Reserve','Trim the Cat','The Strand Arcade','Justice and Police Museum','McKell Park','Lindesay','Vaucluse Bay Range Front Light'}
UA='SydneyBuddy/1.0 (https://github.com/f1sh11/hackathon)'
def fetch(host,params):
    url='https://'+host+'/w/api.php?'+urlencode(dict(params,format='json'))
    request=Request(url,headers={'User-Agent':UA})
    with urlopen(request,timeout=30,context=ssl.create_default_context(cafile='/etc/ssl/cert.pem')) as response:data=json.load(response)
    if 'error' in data:raise ValueError('Wikimedia returned an API error')
    return data

def templates(text):
    depth=0;start=0;i=0
    while i<len(text)-1:
        pair=text[i:i+2]
        if pair=='{{':
            if depth==0:start=i+2
            depth+=1;i+=2
        elif pair=='}}' and depth:
            depth-=1
            if depth==0:yield text[start:i]
            i+=2
        else:i+=1

def split_fields(text):
    result=[];start=0;level=0;i=0
    while i<len(text):
        pair=text[i:i+2]
        if pair in ('{{','[['):level+=1;i+=2;continue
        if pair in ('}}',']]'):level-=1;i+=2;continue
        if text[i]=='|' and level==0:result.append(text[start:i]);start=i+1
        i+=1
    return result+[text[start:]]

def plain(text):
    text=re.sub(r'<!--.*?-->','',text,flags=re.S)
    text=re.sub(r'\[\[(?:File|Image):.*?\]\]','',text,flags=re.I)
    text=re.sub(r'\[\[([^\]|]+)\|([^\]]+)\]\]',lambda m:m[2],text)
    text=re.sub(r'\[\[([^\]]+)\]\]',lambda m:m[1].split('#')[-1],text)
    text=re.sub(r'\[https?://\S+\s+([^\]]+)\]',lambda m:m[1],text)
    # Unsupported inline templates are omitted rather than guessing a bus/train line.
    text=re.sub(r'\{\{[^{}]*\}\}','',text)
    text=re.sub(r'<[^>]+>','',text).replace("'''",'').replace("''",'')
    return re.sub(r'\s+',' ',html.unescape(text)).strip()

def safe_url(value,hosts):
    p=urlparse(value)
    return p.scheme=='https' and p.hostname in hosts and not p.username and not p.password

def photo_records(data,match_kind):
    result=[]
    for page in data.get('query',{}).get('pages',{}).values():
        info=next(iter(page.get('imageinfo',[])),{})
        meta=info.get('extmetadata',{})
        val=lambda k:plain(meta.get(k,{}).get('value',''))
        license=val('LicenseShortName');license_url=val('LicenseUrl');artist=val('Artist');url=info.get('thumburl',info.get('url',''));page_url=info.get('descriptionurl','')
        if info.get('mime') not in ('image/jpeg','image/png','image/webp'):continue
        if not safe_url(url,{'upload.wikimedia.org','thumb.wikimedia.org'}) or not safe_url(page_url,{'commons.wikimedia.org'}):continue
        if not license or not artist or not license_url.startswith('https://'):continue
        # Preserve per-file attribution; only unrestricted reuse licenses enter the pack.
        if not (license.startswith('CC BY') or license in ('CC0','Public domain')) or any(x in license.upper() for x in ('NC','ND')):continue
        result.append({'url':url,'pageUrl':page_url,'title':plain(page.get('title','').removeprefix('File:')),'artist':artist,'license':license,'licenseUrl':license_url,'source':'Wikimedia Commons','matchKind':match_kind})
    return result

def listings(data,page,now):
    parsed=data['parse'];revision=parsed['revid'];source='https://en.wikivoyage.org/w/index.php?'+urlencode({'title':page,'oldid':revision})
    for template in templates(parsed['wikitext']['*']):
        parts=split_fields(template)
        if parts[0].strip() not in ('see','do'):continue
        fields={}
        for part in parts[1:]:
            key,sep,value=part.partition('=')
            if sep:fields[key.strip()]=value.strip()
        name=plain(fields.get('name',''))
        if name not in NAMES:continue
        try:lat=float(fields.get('lat',''));lng=float(fields.get('long',''))
        except ValueError:continue
        if not (math.isfinite(lat) and math.isfinite(lng) and -34.2<=lat<=-33.5 and 150.5<=lng<=151.5):continue
        content=plain(fields.get('content',''));directions=plain(fields.get('directions',''))
        identifier='gem:'+hashlib.sha256((page+'|'+name).encode()).hexdigest()[:24]
        yield {'id':identifier,'name':name,'category':'風景','indoor':False,'visitMinutes':60,'cost':0,'lat':lat,'lng':lng,
               'source':'Wikivoyage','hiddenGem':True,'poiKind':'attraction','reason':'社群指南探索清單；未驗證熱門程度',
               'description':content,'note':'到達方式及開放情況請出發前確認；此為社群介紹，非即時評論。費用與排隊未知。',
               'openingHoursText':plain(fields.get('hours','')),'address':plain(fields.get('address','')),
               'website':fields.get('url','') if fields.get('url','').startswith('https://') else '',
               'accessStatus':'unverified','reviews':[],'reviewsStatus':'not-provided-by-source',
               'guide':{'directions':directions,'directionsStatus':'community-guide' if directions else 'unknown',
                        'sourceUrl':source,'revision':str(revision),'license':'CC BY-SA 4.0',
                        'licenseUrl':'https://creativecommons.org/licenses/by-sa/4.0/',
                        'attribution':'Wikivoyage contributors · '+page,'retrievedAt':now,
                        'coordinateSource':source},'photos':[], '_image':fields.get('image','')}

def match_nearby_photos(place,photos):
    tokens=set(re.findall(r'[a-z]{4,}',place['name'].lower()))-{'reserve','garden','point','park','front','light'}
    if not tokens:return []
    scored=[(sum(t in photo['title'].lower() for t in tokens),photo) for photo in photos]
    return [photo for score,photo in sorted(scored,key=lambda item:(-item[0],item[1]['title'])) if score>=min(2,len(tokens))]

def enrich(place):
    image=place.pop('_image','')
    params={'action':'query','prop':'imageinfo','iiprop':'url|extmetadata|mime','iiurlwidth':640}
    if image:
        params['titles']='File:'+image
        photos=photo_records(fetch('commons.wikimedia.org',params),'guide-file')
    else:
        params.update({'generator':'geosearch','ggsprimary':'all','ggsnamespace':6,'ggsradius':150,'ggslimit':8,'ggscoord':f"{place['lat']}|{place['lng']}"})
        photos=photo_records(fetch('commons.wikimedia.org',params),'nearby-unverified')
        photos=match_nearby_photos(place,photos)
    place['photos']=photos[:3]
    return place

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--output',type=Path,default=ROOT/'infra/zetaris/data');args=parser.parse_args()
    now=datetime.now(timezone.utc).isoformat();places=[];errors=[]
    for page in PAGES:
        data=fetch('en.wikivoyage.org',{'action':'parse','page':page,'prop':'wikitext|revid','redirects':1})
        for place in listings(data,page,now):
            try:enrich(place)
            except Exception as e:place.pop('_image',None);errors.append({'id':place['id'],'stage':'photo','error':type(e).__name__})
            places.append(place);time.sleep(.25)
    if not places:raise ValueError('No real community places returned; refuse empty export')
    args.output.mkdir(parents=True,exist_ok=True)
    payload={'schemaVersion':1,'source':'Wikivoyage + Wikimedia Commons','generatedAt':now,'places':places,'photoErrors':errors,'scope':'Sydney editorial exploration candidates; popularity and access unverified'}
    (args.output/'hidden-gems.json').write_text(json.dumps(payload,ensure_ascii=False,indent=2)+'\n')
    with (args.output/'hidden-gems.csv').open('w',newline='') as f:
        writer=csv.writer(f);writer.writerow(['id','name','lat','lng','payload'])
        for p in places:writer.writerow([p['id'],p['name'],p['lat'],p['lng'],json.dumps(p,ensure_ascii=False,separators=(',',':'))])
    print(json.dumps({'places':len(places),'photos':sum(len(p['photos']) for p in places),'photoErrors':len(errors),'reviews':0,'generatedAt':now},ensure_ascii=False))
if __name__=='__main__':main()
