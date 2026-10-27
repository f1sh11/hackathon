import copy,importlib.util,json,unittest
from pathlib import Path
root=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('aws_render',root/'scripts/render_aws_colocation.py');mod=importlib.util.module_from_spec(spec);spec.loader.exec_module(mod)
class ColocationSafetyTests(unittest.TestCase):
 def setUp(self):
  self.template=json.loads((root/'infra/aws-eks/colocation.template.json').read_text());self.policy=json.loads((root/'infra/aws-eks/deployment-policy.json').read_text());self.images={name:'example.invalid/'+name+'@sha256:'+'a'*64 for name in ['api','db','gateway']}
 def test_rejects_public_service_extra_replicas_node_changes_and_excess_storage(self):
  for kind,edit in [('Service',lambda o:o['spec'].update(type='LoadBalancer')),('StatefulSet',lambda o:o['spec'].update(replicas=2)),('StatefulSet',lambda o:o['spec']['template']['spec']['nodeSelector'].update({'kubernetes.io/hostname':'a-new-node'})),('StatefulSet',lambda o:o['spec']['volumeClaimTemplates'][0]['spec']['resources']['requests'].update(storage='50Gi'))]:
   with self.subTest(kind=kind,edit=edit):
    changed=copy.deepcopy(self.template);edit(next(o for o in changed['items'] if o['kind']==kind))
    with self.assertRaises(ValueError):mod.render(changed,self.policy,self.images)
 def test_rejects_mutable_image_tags(self):
  self.images['api']='example.invalid/api:latest'
  with self.assertRaises(ValueError):mod.render(self.template,self.policy,self.images)
 def test_pins_reviewed_images_without_mutating_source(self):
  result=mod.render(self.template,self.policy,self.images)
  pod=next(o for o in result['items'] if o['kind']=='StatefulSet')['spec']['template']['spec']
  self.assertEqual({c['name']:c['image'] for c in pod['containers']},self.images)
  self.assertFalse(pod['automountServiceAccountToken'])
  self.assertEqual(sum(int(c['resources']['requests']['cpu'][:-1]) for c in pod['containers']),260)
  self.assertIn('REPLACE_WITH_PRIVATE_IMAGE_DIGEST',json.dumps(self.template))
if __name__=='__main__':unittest.main()
