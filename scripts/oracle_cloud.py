#!/usr/bin/env python3
"""Configure the approved Oracle test backend without putting credentials in arguments or Git."""
import argparse
import ipaddress
import json
import re
import shlex
import subprocess
from pathlib import Path

cli=argparse.ArgumentParser(description=__doc__)
cli.add_argument('action',choices=['configure','enable-https'])
cli.add_argument('--host',required=True)
cli.add_argument('--key',required=True,type=Path)
cli.add_argument('--zetaris-config',type=Path)
cli.add_argument('--https-config',type=Path)
args=cli.parse_args()
host=ipaddress.IPv4Address(args.host);key=args.key.resolve()
if not host.is_global or not key.is_file() or key.stat().st_mode&0o077:
    raise ValueError('Use a public VM address and a private SSH key with mode 0600')
options=['-i',str(key),'-o','BatchMode=yes','-o','IdentitiesOnly=yes',
         '-o','StrictHostKeyChecking=yes','-o','UserKnownHostsFile='+str(key.with_suffix('.known_hosts')),
         '-o','ConnectTimeout=20']
target='opc@'+str(host)
def ssh(command,payload=None):
    subprocess.run(['ssh',*options,target,command],input=payload,check=True)

if args.action=='configure':
    if not args.zetaris_config or not args.https_config:
        cli.error('configure requires both private configuration paths')
    for path in [args.zetaris_config,args.https_config]:
        if path.stat().st_mode&0o077:raise ValueError('Private configuration must have mode 0600')
    z=json.loads(args.zetaris_config.read_text())['Zetaris']['Cloud']
    h=json.loads(args.https_config.read_text())
    allowed=['api.'+ip+'.'+domain for ip in [str(host),str(host).replace('.','-')] for domain in ['sslip.io','nip.io']]
    if h['host'] not in allowed or h['url']!='https://'+h['host']:
        raise ValueError('The test hostname must match this VM address')
    if not re.fullmatch(r'[A-Za-z0-9_-]{24,256}',h['accessCode']):raise ValueError('Invalid App access code')
    if not str(z['BaseUrl']).startswith('https://') or not str(z['LoginUrl']).startswith('https://'):
        raise ValueError('Cloud Zetaris requires HTTPS')
    values={'TravelData__ManagedSource':'zetaris-cloud'}
    for name in ['BaseUrl','LoginUrl','Transport','Username','Password','SocialEnabled','RestaurantsEnabled']:
        if name not in z:continue
        value=str(z[name]).lower() if isinstance(z[name],bool) else str(z[name])
        if '\n' in value or '\r' in value or '\0' in value:raise ValueError('Invalid configuration value')
        values['Zetaris__Cloud__'+name]=value
    root=Path(__file__).resolve().parents[1]
    payload={'zetaris-cloud.env':'\n'.join(k+'='+v for k,v in values.items())+'\n',
             'https.env':'PUBLIC_HOST='+h['host']+'\nAPP_ACCESS_TOKEN='+h['accessCode']+'\n',
             'Caddyfile':(root/'infra/oracle/Caddyfile').read_text(),
             'caddy.container':(root/'infra/oracle/caddy.container').read_text()}
    remote="""import json,os,sys
from pathlib import Path
os.umask(0o077)
data=json.load(sys.stdin)
root=Path('/opt/sydneybuddy');root.mkdir(mode=0o711,exist_ok=True)
for name in ['zetaris-cloud.env','https.env','Caddyfile']:
 p=root/name;p.write_text(data[name]);p.chmod(0o600 if name.endswith('.env') else 0o644)
p=Path('/etc/containers/systemd/sydneybuddy-https.container');p.parent.mkdir(parents=True,exist_ok=True);p.write_text(data['caddy.container']);p.chmod(0o644)
print('Private configurations and HTTPS service installed; credentials not displayed.')
"""
    ssh('sudo python3 -c '+shlex.quote(remote),json.dumps(payload).encode())
else:
    # Run after installing the managed backend image. Existing database/app volumes remain intact.
    ssh("""set -eu
sudo test -s /opt/sydneybuddy/zetaris-cloud.env
sudo test -s /opt/sydneybuddy/https.env
sudo podman pull docker.io/library/caddy:2
if sudo firewall-cmd --state >/dev/null 2>&1; then
 sudo firewall-cmd --permanent --add-service=http
 sudo firewall-cmd --permanent --add-service=https
 sudo firewall-cmd --reload
 # firewalld reload clears container forwarding rules; restore only existing Podman mappings.
 sudo podman network reload --all
fi
sudo systemctl daemon-reload
sudo systemctl restart sydneybuddy-https.service
sudo systemctl is-active sydneybuddy-api.service sydneybuddy-db.service sydneybuddy-https.service
""")
