#!/usr/bin/env python3
"""Deploy a prebuilt x86 backend image to the approved Oracle Linux E2 Micro."""
import argparse,hashlib,ipaddress,subprocess
from pathlib import Path
cli=argparse.ArgumentParser(description=__doc__)
cli.add_argument('--image-tag',choices=['oracle-amd64','oracle-managed'],default='oracle-amd64');cli.add_argument('--host',required=True);cli.add_argument('--key',type=Path,required=True);cli.add_argument('--image',type=Path,required=True)
args=cli.parse_args();host=ipaddress.IPv4Address(args.host);key=args.key.resolve();image=args.image.resolve()
if not host.is_global or not key.is_file() or key.stat().st_mode&0o077 or not image.is_file():raise ValueError('Invalid host, key permissions, or image archive')
options=['-i',str(key),'-o','IdentitiesOnly=yes','-o','StrictHostKeyChecking=accept-new','-o','UserKnownHostsFile='+str(key.with_suffix('.known_hosts')),'-o','ConnectTimeout=15']
target='opc@'+str(host)
def ssh(command):subprocess.run(['ssh',*options,target,command],check=True)
def copy(path,dest):subprocess.run(['scp',*options,str(path),target+':'+dest],check=True)
ssh("""set -eu; if [ ! -e /var/swap-sydneybuddy ]; then sudo fallocate -l 2G /var/swap-sydneybuddy; sudo chmod 600 /var/swap-sydneybuddy; sudo mkswap /var/swap-sydneybuddy; sudo swapon /var/swap-sydneybuddy; echo "/var/swap-sydneybuddy none swap defaults 0 0" | sudo tee -a /etc/fstab >/dev/null; fi; if ! command -v podman >/dev/null 2>&1; then sudo dnf -y --disablerepo='*' --enablerepo=ol9_baseos_latest --enablerepo=ol9_appstream --setopt=install_weak_deps=False install podman; fi; sudo install -d -m 711 /opt/sydneybuddy; sudo install -d -m 700 -o opc -g opc /opt/sydneybuddy/incoming; cat /etc/os-release; uname -m""")
digest=hashlib.file_digest(image.open('rb'),'sha256').hexdigest();remote='/opt/sydneybuddy/incoming/'+digest+'.tar.gz'
copy(image,remote);copy(Path(__file__).resolve().parents[1]/'infra/oracle/install-micro.py','/opt/sydneybuddy/incoming/install-micro.py')
ssh("set -eu; echo '"+digest+'  '+remote+"' | sha256sum -c -; sudo podman load -i "+remote+"; sudo env SYDNEYBUDDY_IMAGE_TAG="+args.image_tag+" python3 /opt/sydneybuddy/incoming/install-micro.py")
