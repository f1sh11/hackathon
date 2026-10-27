"""Bundle attributed Wikimedia landmark thumbnails; never invent place photos."""
import concurrent.futures,html,json,re,subprocess,urllib.parse
from pathlib import Path
OUT=Path(__file__).resolve().parents[1]/'ios/Resources/map-thumbnails';OUT.mkdir(parents=True,exist_ok=True)
EXISTING=json.loads((OUT/'manifest.json').read_text()) if (OUT/'manifest.json').exists() else []
PLACES=[
 ('opera','Sydney_Opera_House',['Sydney Opera House','悉尼歌剧院','雪梨歌劇院'],-33.8571,151.2153,500),
 ('gardens','Royal_Botanic_Garden,_Sydney',['Royal Botanic Garden','皇家植物园','皇家植物園'],-33.8642,151.2166,800),
 ('rocks','The_Rocks,_New_South_Wales',['The Rocks','岩石区','岩石區'],-33.8592,151.2085,600),
 ('park','Centennial_Park,_New_South_Wales',['Centennial Park','百年纪念公园','百年紀念公園'],-33.9013,151.2359,1800),
 ('bondi','Bondi_Beach',['Bondi Beach','邦迪海滩','邦迪海灘'],-33.8915,151.2767,800),
 ('icebergs','Bondi_Icebergs_Club',['Icebergs'],-33.8952732,151.2744078,250),
 ('manly','Manly,_New_South_Wales',['Manly Wharf','Manly waterfront','曼利'],-33.8008,151.2844,800),
 ('shelly','Shelly_Beach_(Manly)',['Shelly Beach'],-33.8005,151.2968,500),
 ('gallery','Art_Gallery_of_New_South_Wales',['Art Gallery of NSW','Art Gallery of New South Wales','新南威尔士美术馆'],-33.8688,151.2173,400),
 ('quay','Circular_Quay',['Circular Quay','环形码头','環形碼頭'],-33.8610,151.2111,600),
]
def get(url):
 p=subprocess.run(['curl','-fsSL','--max-time','20','-A','SydneyBuddy/1.0 (landmark photo credits included)',url],capture_output=True,check=True);return p.stdout
def clean(text):return html.unescape(re.sub('<[^>]+>','',text)).strip()
def fetch(item):
 key,title,aliases,lat,lng,radius=item
 try:
  summary=json.loads(get('https://en.wikipedia.org/api/rest_v1/page/summary/'+urllib.parse.quote(title)))
  original=summary.get('originalimage',{}).get('source','');name=urllib.parse.unquote(urllib.parse.urlsplit(original).path.split('/')[-1])
  # Thumbnail URLs may contain a size prefix; use the original filename from the summary title image path.
  name=re.sub(r'^\d+px-','',name)
  query=urllib.parse.urlencode({'action':'query','format':'json','prop':'imageinfo','iiprop':'url|extmetadata','iiurlwidth':240,'titles':'File:'+name})
  pages=json.loads(get('https://commons.wikimedia.org/w/api.php?'+query))['query']['pages']
  info=next(v['imageinfo'][0] for v in pages.values() if v.get('imageinfo'))
  meta=info['extmetadata'];license=clean(meta.get('LicenseShortName',{}).get('value',''));artist=clean(meta.get('Artist',{}).get('value',''))
  if not (license.startswith('CC BY') or license.startswith('CC0') or license in ['Public domain','PD']):raise ValueError('Unsupported or missing license: '+license)
  data=get(info.get('thumburl') or summary['thumbnail']['source'])
  if not (data.startswith(b'\xff\xd8') or data.startswith(b'\x89PNG')):raise ValueError('Unexpected image type')
  filename=key+('.jpg' if data.startswith(b'\xff\xd8') else '.png');(OUT/filename).write_bytes(data)
  return {'asset':filename,'aliases':aliases,'lat':lat,'lng':lng,'radius':radius,'title':summary['title'],'artist':artist,'license':license,'sourceURL':info['descriptionurl'],'licenseURL':meta.get('LicenseUrl',{}).get('value','')}
 except Exception as error:
  cached=next((x for x in EXISTING if x['asset'].split('.')[0]==key and (OUT/x['asset']).exists()),None)
  print(key,'using cached' if cached else 'skipped:',str(error)[:70]);return cached
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:results=[x for x in pool.map(fetch,PLACES) if x]
(OUT/'manifest.json').write_text(json.dumps(results,ensure_ascii=False,indent=2)+'\n')
(OUT/'CREDITS.txt').write_text('Wikimedia Commons photographs, displayed as cropped map thumbnails. Tap a photo marker for attribution and its source page.\n\n'+'\n\n'.join(f'{x["title"]}\n{x["artist"]}\n{x["license"]}\n{x["sourceURL"]}\n{x["licenseURL"]}' for x in results)+'\n')
print('Bundled',len(results),'attributed landmark photos.')
for x in results:print(x['asset'],x['title'],x['license'])
