#!/usr/bin/env python3
"""Deploy a reviewed backend bundle to your Ubuntu VM, or open its private SSH test tunnel."""
import argparse,hashlib,ipaddress,json,shlex,subprocess,tarfile
from pathlib import Path

def verify_bundle(path):
    checksum=path.with_suffix(path.suffix+'.sha256').read_text().split()[0]
    if hashlib.sha256(path.read_bytes()).hexdigest()!=checksum:raise ValueError('Bundle checksum mismatch')
    with tarfile.open(path,'r:gz') as archive:
        members=archive.getmembers();names=[m.name for m in members]
        if len(names)!=len(set(names)) or sum(m.size for m in members)>100_000_000:raise ValueError('Invalid bundle size or duplicate path')
        if any(not m.isfile() or Path(m.name).is_absolute() or '..' in Path(m.name).parts or Path(m.name).name in ('.env','.secrets.json') for m in members):raise ValueError('Unsafe bundle member')
        manifest=json.load(archive.extractfile('oracle-release-manifest.json'))
        if manifest.get('privateUserDataIncluded') is not False:raise ValueError('Bundle must exclude private user data')
        if set(names)!=set(manifest['files'])|{'oracle-release-manifest.json'}:raise ValueError('Manifest file list mismatch')
        for name,digest in manifest['files'].items():
            if hashlib.sha256(archive.extractfile(name).read()).hexdigest()!=digest:raise ValueError('Bundle file checksum mismatch')
    return checksum

def remote_script(digest):
    # All variable remote paths derive only from a verified hex digest.
    if len(digest)!=64 or any(c not in '0123456789abcdef' for c in digest):raise ValueError('Invalid digest')
    release='/opt/sydneybuddy/releases/'+digest[:16]
    return f'''set -eu
umask 077
cd /opt/sydneybuddy
echo '{digest}  incoming/{digest}.tar.gz' | sha256sum -c -
if [ ! -d '{release}' ]; then
  mkdir -m 700 '{release}'
  tar -xzf 'incoming/{digest}.tar.gz' -C '{release}'
fi
if [ ! -f /opt/sydneybuddy/.env ]; then
  python3 -c 'import secrets; print("POSTGRES_PASSWORD="+secrets.token_hex(32)); print("API_PORT=8787")' > /opt/sydneybuddy/.env
fi
chmod 600 /opt/sydneybuddy/.env
if [ ! -e '{release}/infra/oracle/.env' ]; then
  ln -s /opt/sydneybuddy/.env '{release}/infra/oracle/.env'
fi
sudo docker compose --env-file /opt/sydneybuddy/.env -f '{release}/infra/oracle/compose.yaml' up -d --build --wait --wait-timeout 180
curl --fail --silent --show-error http://127.0.0.1:8787/health
ln -sfn '{release}' /opt/sydneybuddy/current
'''

def main():
    cli=argparse.ArgumentParser(description=__doc__);cli.add_argument('action',choices=['deploy','status','tunnel'])
    cli.add_argument('--host',required=True);cli.add_argument('--key',type=Path,required=True)
    cli.add_argument('--bundle',type=Path);cli.add_argument('--local-port',type=int,default=8790);cli.add_argument('--dry-run',action='store_true')
    args=cli.parse_args();host=ipaddress.IPv4Address(args.host)
    if not host.is_global:raise ValueError('Use the public IPv4 address shown for your Oracle VM')
    key=args.key.resolve()
    if not key.is_file():raise ValueError('SSH private key is missing')
    if key.stat().st_mode & 0o077:raise ValueError('SSH private key must have private permissions (chmod 600)')
    options=['-i',str(key),'-o','IdentitiesOnly=yes','-o','StrictHostKeyChecking=accept-new','-o','UserKnownHostsFile='+str(key.with_suffix('.known_hosts')),'-o','ConnectTimeout=15']
    destination='ubuntu@'+str(host)
    if args.action=='deploy':
        if args.bundle is None:raise ValueError('--bundle is required')
        digest=verify_bundle(args.bundle)
        if args.dry_run:print(json.dumps({'host':str(host),'verifiedBundle':str(args.bundle),'sha256':digest,'apiExposure':'SSH tunnel only','existingPersonalDataTransferred':False}));return
        subprocess.run(['ssh',*options,destination,'set -eu; if ! sudo docker compose version >/dev/null 2>&1; then sudo apt-get update && sudo apt-get -o DPkg::Lock::Timeout=180 install -y ca-certificates curl docker.io docker-compose-v2; fi; sudo systemctl enable --now docker && sudo install -d -m 700 -o ubuntu -g ubuntu /opt/sydneybuddy && mkdir -p /opt/sydneybuddy/incoming /opt/sydneybuddy/releases && sudo docker compose version'],check=True)
        subprocess.run(['scp',*options,str(args.bundle),destination+':/opt/sydneybuddy/incoming/'+digest+'.tar.gz'],check=True)
        subprocess.run(['ssh',*options,destination,'sh -s'],input=remote_script(digest).encode(),check=True)
    elif args.action=='status':
        subprocess.run(['ssh',*options,destination,"sudo docker compose --env-file /opt/sydneybuddy/.env -f /opt/sydneybuddy/current/infra/oracle/compose.yaml ps && curl --fail --silent --show-error http://127.0.0.1:8787/health"],check=True)
    else:
        if not 1024<=args.local_port<=65535:raise ValueError('Invalid local port')
        print(f'Private API: http://127.0.0.1:{args.local_port} (keep this process running)',flush=True)
        subprocess.run(['ssh',*options,'-NT','-o','ExitOnForwardFailure=yes','-o','ServerAliveInterval=30','-L',f'127.0.0.1:{args.local_port}:127.0.0.1:8787',destination],check=True)

if __name__=='__main__':main()
