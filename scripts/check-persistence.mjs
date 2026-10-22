import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';

const base=(process.env.ROAM_URL||'http://127.0.0.1:5080').replace(/\/$/,'');
const mode=process.argv[2];
const filename=process.argv[3]||'artifacts/persistence-before-restart.json';
if(!['snapshot','verify'].includes(mode))throw new Error('Usage: node scripts/check-persistence.mjs snapshot|verify [file]');
async function get(path){
  const response=await fetch(base+path,{signal:AbortSignal.timeout(10000)});
  assert.equal(response.status,200,`${path} returned ${response.status}`);
  return response.json();
}
const bootstrap=await get('/api/bootstrap');
assert.ok(bootstrap.profile,'A saved profile must exist before checking persistence.');
assert.ok(bootstrap.trips.length>0,'At least one saved trip is required.');
const snapshot={profile:bootstrap.profile,trips:[]};
for(const trip of [...bootstrap.trips].sort((a,b)=>a.id.localeCompare(b.id))){
  snapshot.trips.push(await get(`/api/trips/${trip.id}`));
}
if(mode==='snapshot'){
  await mkdir(dirname(filename),{recursive:true});
  await writeFile(filename,JSON.stringify(snapshot,null,2));
  console.log(`Saved persistence snapshot: profile + ${snapshot.trips.length} tasks with full version history.`);
}else{
  const before=JSON.parse(await readFile(filename,'utf8'));
  assert.deepEqual(snapshot,before,'Profile, tasks or version history changed across the container restart.');
  console.log(`PASS: profile + ${snapshot.trips.length} task IDs and complete version history survived restart unchanged.`);
}
