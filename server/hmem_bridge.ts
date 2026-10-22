import fs from 'node:fs';
import { HMEM } from './vendor/hmem/src/index.ts';
// 每次操作使用一个进程；后端桥接器串行访问持久化状态。
// One process per operation; the backend bridge serializes access to persistent state.
const input=JSON.parse(fs.readFileSync(0,'utf8'));
const engine=new HMEM({actor:'sydney-buddy-user',persistDir:input.directory});
let result:unknown;
switch(input.action){
 case 'add': result=engine.add({content:input.content,type:'preference',layer:'long_term',tags:['project:travel','sydney','preference'],source:'user_confirmed',confidence:1,chatId:'local-user',provenance:{origin:'manual',learnedAt:Date.now(),label:'用户在 App 中确认保存旅行偏好'}});break;
 case 'query':result=engine.query(input.query,{chatId:'local-user'});engine.store.persist();break;
 case 'delete':if(engine.store.get(input.id))engine.store.delete(input.id,'user revoked');result={deleted:true};break;
 case 'list':result=engine.store.all();break;
 default:throw new Error('Unsupported memory operation');
}
process.stdout.write(JSON.stringify(result));

