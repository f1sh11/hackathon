#!/usr/bin/env python3
"""Run as root on Oracle Linux 9 after loading the reviewed amd64 image."""
import os,secrets,subprocess,time
from pathlib import Path
os.umask(0o077)
tag=os.environ.get('SYDNEYBUDDY_IMAGE_TAG','oracle-amd64')
if tag not in ('oracle-amd64','oracle-managed'):raise ValueError('Unsupported image tag')
subprocess.run(['podman','tag','docker.io/library/sydneybuddy-api:'+tag,'localhost/sydneybuddy-api:'+tag],check=True)
root=Path('/opt/sydneybuddy');root.mkdir(mode=0o711,exist_ok=True)
os.chmod(root,0o711)
env=root/'database.env'
if not env.exists():
    env.write_text('POSTGRES_DB=sydneybuddy\nPOSTGRES_USER=sydneybuddy\nPOSTGRES_PASSWORD='+secrets.token_hex(32)+'\n')
os.chmod(env,0o600)
values=dict(line.split('=',1) for line in env.read_text().splitlines() if '=' in line)
api=root/'api.env'
if not api.exists():
    api.write_text('ConnectionStrings__SydneyBuddy=Host=db;Port=5432;Database=sydneybuddy;Username=sydneybuddy;Password='+values['POSTGRES_PASSWORD']+';Maximum Pool Size=10\nDOTNET_GCHeapHardLimit=0x18000000\nNODE_OPTIONS=--max-old-space-size=64\n')
# Upgrade the old 128/192 MiB heap caps for inventory and Meterless planning.
# Keep 256 MiB headroom for native allocations and the bounded Node helper.
# Preserve custom settings, database credentials and other private environment values.
content=api.read_text()
for previous in ('0x08000000','0x0C000000','0x14000000'):
    content=content.replace('DOTNET_GCHeapHardLimit='+previous,'DOTNET_GCHeapHardLimit=0x18000000')
api.write_text(content)
os.chmod(api,0o600)
units=Path('/etc/containers/systemd');units.mkdir(parents=True,exist_ok=True)
files={
'sydneybuddy.network':'[Network]\nNetworkName=sydneybuddy\n',
'sydneybuddy-db.volume':'[Volume]\nVolumeName=sydneybuddy-postgres-data\n',
'sydneybuddy-state.volume':'[Volume]\nVolumeName=sydneybuddy-app-state\n',
'sydneybuddy-db.container':'''[Unit]
Description=Sydney Buddy private PostgreSQL
[Container]
Image=docker.io/library/postgres:17
ContainerName=sydneybuddy-db
Network=sydneybuddy.network
NetworkAlias=db
Volume=sydneybuddy-db.volume:/var/lib/postgresql/data
EnvironmentFile=/opt/sydneybuddy/database.env
Exec=postgres -c shared_buffers=32MB -c max_connections=20 -c work_mem=1MB
PodmanArgs=--memory=120m --log-opt=max-size=10mb
[Service]
Restart=always
TimeoutStartSec=300
[Install]
WantedBy=multi-user.target
''',
'sydneybuddy-api.container':'''[Unit]
Description=Sydney Buddy private backend
Requires=sydneybuddy-db.service
After=sydneybuddy-db.service
[Container]
Image=localhost/sydneybuddy-api:oracle-amd64
ContainerName=sydneybuddy-api
Network=sydneybuddy.network
Volume=sydneybuddy-state.volume:/var/lib/sydneybuddy
EnvironmentFile=/opt/sydneybuddy/api.env
PublishPort=127.0.0.1:8787:8787
DropCapability=ALL
NoNewPrivileges=true
PodmanArgs=--memory=640m --log-opt=max-size=10mb
[Service]
Restart=always
TimeoutStartSec=180
[Install]
WantedBy=multi-user.target
'''}
files['sydneybuddy-api.container']=files['sydneybuddy-api.container'].replace('localhost/sydneybuddy-api:oracle-amd64','localhost/sydneybuddy-api:'+tag)
if (root/'zetaris-cloud.env').exists():
    files['sydneybuddy-api.container']=files['sydneybuddy-api.container'].replace('EnvironmentFile=/opt/sydneybuddy/api.env','EnvironmentFile=/opt/sydneybuddy/api.env\nEnvironmentFile=/opt/sydneybuddy/zetaris-cloud.env')
for name,content in files.items():(units/name).write_text(content)
subprocess.run(['systemctl','daemon-reload'],check=True)
subprocess.run(['systemctl','start','sydneybuddy-db.service'],check=True)
for attempt in range(60):
    if subprocess.run(['podman','exec','sydneybuddy-db','pg_isready','-U','sydneybuddy','-d','sydneybuddy'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL).returncode==0:break
    time.sleep(2)
else:raise RuntimeError('Database not ready')
subprocess.run(['systemctl','restart','sydneybuddy-api.service'],check=True)
for attempt in range(90):
    result=subprocess.run(['curl','--fail','--silent','http://127.0.0.1:8787/health'],capture_output=True)
    if result.returncode==0:
        print(result.stdout.decode());break
    time.sleep(2)
else:raise RuntimeError('Backend not ready; inspect journalctl -u sydneybuddy-api')
