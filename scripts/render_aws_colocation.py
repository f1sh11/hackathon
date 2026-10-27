#!/usr/bin/env python3
"""Render the approved single-node AWS trial; never contacts AWS or reads private data."""
import argparse,copy,json,re
from pathlib import Path

def render(template,policy,images):
    data=copy.deepcopy(template)
    assert data['kind']=='List'
    assert not policy['newComputeHostsAllowed'] and not policy['newLoadBalancersAllowed']
    storage=0
    for obj in data['items']:
        if obj['kind'] not in {'ConfigMap','Service','StatefulSet','StorageClass'}: raise ValueError('Unsupported resource')
        if obj['kind']=='StorageClass':
            if obj['metadata']['name']!=policy['storageClass'] or obj['provisioner']!='ebs.csi.aws.com' or obj['parameters']!={'type':'gp2','encrypted':'true','csi.storage.k8s.io/fstype':'ext4'}: raise ValueError('Encrypted gp2 storage required')
            continue
        if obj['metadata']['namespace']!=policy['namespace']: raise ValueError('Namespace mismatch')
        if obj['kind']=='Service' and obj['spec']['type']!='ClusterIP': raise ValueError('Public service prohibited')
        if obj['kind']!='StatefulSet': continue
        spec=obj['spec'];pod=spec['template']['spec']
        if spec['replicas']!=1 or pod['nodeSelector']!={'kubernetes.io/hostname':policy['node']}: raise ValueError('Existing node and single replica required')
        if pod.get('automountServiceAccountToken') is not False: raise ValueError('Workload must not receive cluster credentials')
        for container in pod['containers']:
            image=images[container['name']]
            if not re.fullmatch(r'[a-zA-Z0-9./_:-]+@sha256:[a-f0-9]{64}',image): raise ValueError('Each image requires a digest')
            container['image']=image
        for claim in spec['volumeClaimTemplates']:
            v=claim['spec']['resources']['requests']['storage']
            if not re.fullmatch(r'[1-9][0-9]*Gi',v) or claim['spec']['storageClassName']!=policy['storageClass']: raise ValueError('Reviewed storage class and GiB sizes required')
            storage+=int(v[:-2])
    estimate=storage*policy['gp2USDPerGiBMonth']+policy['maximumPrivateImageGB']*policy['ecrUSDPerGBMonth']
    if storage>policy['maximumAdditionalStorageGiB'] or estimate>policy['maximumAdditionalMonthlyStorageUSD']: raise ValueError('Approved storage budget exceeded')
    return data

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--api-image',required=True);parser.add_argument('--db-image',required=True);parser.add_argument('--gateway-image',required=True);parser.add_argument('--output',required=True,type=Path)
    args=parser.parse_args();root=Path(__file__).resolve().parents[1]/'infra/aws-eks'
    result=render(json.loads((root/'colocation.template.json').read_text()),json.loads((root/'deployment-policy.json').read_text()),dict(api=args.api_image,db=args.db_image,gateway=args.gateway_image))
    args.output.write_text(json.dumps(result,indent=2)+'\n')
    print('Rendered approved trial manifest; no cloud changes performed.')
if __name__=='__main__': main()
