#!/usr/bin/env python3
"""Upload a planning JSON file and save its result; never print the shared App access code."""
import argparse,json,ssl,sys,urllib.request,urllib.error,urllib.parse,uuid
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,*args):return None

def main():
    p=argparse.ArgumentParser(description=__doc__);g=p.add_mutually_exclusive_group(required=True);g.add_argument('--input',type=Path);g.add_argument('--sample',type=Path,help='Download a fresh request example')
    p.add_argument('--output',type=Path,default=Path('planning-result.json'));p.add_argument('--cloud',action='store_true');p.add_argument('--upload',action='store_true',help='Use multipart file upload instead of a JSON body');p.add_argument('--connection-file',type=Path,default=ROOT.parent/'runtime/aws-eks-api/connection.private.json');a=p.parse_args()
    headers={'X-Travel-Data-Source':'zetaris-cloud'} if a.cloud else {}
    if a.cloud:
        config=json.loads(a.connection_file.read_text());base=config['url'].rstrip('/');headers['Authorization']='Bearer '+config['accessCode']
        u=urllib.parse.urlsplit(base)
        if u.scheme!='https' or u.username or u.password or u.query or u.fragment:raise ValueError('Cloud URL must be HTTPS without credentials, query or fragment')
    else:base='http://127.0.0.1:8787'
    context=ssl.create_default_context()
    try:
        import certifi
        context=ssl.create_default_context(cafile=certifi.where())
    except ImportError:pass
    opener=urllib.request.build_opener(urllib.request.HTTPSHandler(context=context),NoRedirect())
    if a.sample:
        request=urllib.request.Request(base+'/planning/test/example',headers=headers);target=a.sample
    else:
        raw=a.input.read_bytes();obj=json.loads(raw)
        if not isinstance(obj,dict):raise ValueError('Input must be a JSON object with planning parameters')
        raw=json.dumps(obj,ensure_ascii=False).encode()
        if len(raw)>200000:raise ValueError('Input exceeds 200000 bytes')
        if a.upload:
            boundary='planning-'+uuid.uuid4().hex
            body=(f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="request.json"\r\nContent-Type: application/json\r\n\r\n'.encode()+raw+f'\r\n--{boundary}--\r\n'.encode())
            headers['Content-Type']='multipart/form-data; boundary='+boundary
        else:body=raw;headers['Content-Type']='application/json'
        request=urllib.request.Request(base+'/planning/test?download=true',data=body,headers=headers);target=a.output
    try:
        with opener.open(request,timeout=240) as response:
            if response.headers.get_content_disposition()!='attachment':raise ValueError('Server did not return a downloadable JSON attachment')
            result=response.read();json.loads(result)
    except urllib.error.HTTPError as e:
        try:message=json.loads(e.read()).get('error','Request rejected')
        except (ValueError,AttributeError):message='Request rejected'
        raise ValueError(f'HTTP {e.code}: {message}') from None
    target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(result)
    summary=json.loads(result)
    print('Saved JSON: '+str(target))
    if a.input:print(json.dumps({'skillVersion':summary.get('skillVersion'),'saved':summary.get('saved'),'days':len(summary.get('draft',{}).get('days',[]))},ensure_ascii=False))
if __name__=='__main__':
    try:main()
    except (ValueError,OSError,KeyError) as e:sys.exit(str(e))
