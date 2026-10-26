import importlib.util,json,sys,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
scripts=Path(__file__).parents[1];sys.path.insert(0,str(scripts))
spec=importlib.util.spec_from_file_location('ndp_files',scripts/'prepare_zetaris_ndp_files.py');ndp=importlib.util.module_from_spec(spec);spec.loader.exec_module(ndp)
class NdpFilesTests(unittest.TestCase):
    def test_batches_are_complete_json_arrays_and_preserve_every_record(self):
        rows=[{'id':str(i),'payload':json.dumps({'name':'藍山 '+str(i),'description':'x'*200},ensure_ascii=False)} for i in range(80)]
        with tempfile.TemporaryDirectory() as tmp:
            target=Path(tmp)/'output'
            with patch.object(ndp,'read_snapshot',return_value=({},{})),patch.object(ndp,'grouped_rows',return_value={'travel_osm_places':rows}):
                result=ndp.materialize(Path('unused'),target,'dffs://poi-test',4096)
            restored=[]
            for item in result['files']:
                raw=(target/item['file']).read_bytes();self.assertLessEqual(len(raw),4096)
                records=json.loads(raw);self.assertIsInstance(records,list);restored.extend(records)
            self.assertEqual(restored,rows);self.assertEqual(result['counts']['travel_osm_places'],80)
            self.assertGreater(len(result['files']),1)
if __name__=='__main__':unittest.main()
