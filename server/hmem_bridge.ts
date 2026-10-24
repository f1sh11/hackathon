import fs from 'node:fs';
import { HMEM } from './vendor/hmem/src/index.ts';
// Application extraction supplies reviewed, schema-validated candidates to the official miner.
// Candidates stay in a per-session working layer; only explicit consent writes long_term.
const input=JSON.parse(fs.readFileSync(0,'utf8'));
const candidates=Array.isArray(input.candidates) ? input.candidates : [];
const engine=new HMEM({actor:'sydney-buddy-user',persistDir:input.directory,
 minerFn:input.action==='stage' ? async()=>JSON.stringify(candidates.map((c:unknown)=>JSON.stringify(c))) : undefined});
const pendingChat='pet-session:'+input.sessionId;
function discardPending(){for(const record of engine.store.all())if(record.chatId===pendingChat&&record.layer!=='long_term')engine.store.delete(record.id,'session candidates replaced or cleared');}
let result:unknown;
switch(input.action){
 case 'add': {
  const dna=input.travelDna;
  result=engine.add({content:input.content,type:'preference',layer:'long_term',
   tags:['project:travel','preference','status:confirmed',...(dna ? ['dimension:'+dna.dimension,'key:'+dna.key]:[])],source:'user_confirmed',confidence:dna?.confidence ?? 1,chatId:'local-user',
   provenance:{origin:dna?.source==='user_correction' ? 'user_correction':dna ? 'chat_message':'manual',learnedAt:Date.now(),label:dna ? JSON.stringify({label:'用户确认保存旅行偏好',travelDna:dna}):'用户确认保存旅行偏好',refs:dna?.sourceMessageId ? [dna.sourceMessageId]:[]}});
  break;
 }
 case 'stage': {
  discardPending();
  const mined=await engine.mining.mineInteraction('chat_message','Travel preference candidates',{chatId:pendingChat,layer:'working'});
  result={candidates:mined.map((record,index)=>engine.store.update(record.id,{source:candidates[index].source,confidence:candidates[index].confidence,
   tags:['project:travel','status:awaiting_confirmation','candidate:'+candidates[index].id],
   provenance:{origin:candidates[index].source==='ai_inference' ? 'model_response':'chat_message',learnedAt:Date.now(),label:'未确认候选，不参与长期偏好检索',refs:[candidates[index].sourceMessageId]}}))};
  break;
 }
 case 'discard_session':discardPending();result={deleted:true};break;
 case 'query': {
  const recalled=engine.query(input.query,{chatId:'local-user'});
  const memories=recalled.memories.filter(r=>r.memory.layer==='long_term'&&r.memory.source==='user_confirmed');
  result={...recalled,memories,context:memories.map(r=>r.memory.content).join('\n')};engine.store.persist();break;
 }
 case 'delete':if(engine.store.get(input.id))engine.store.delete(input.id,'user revoked');result={deleted:true};break;
 case 'list':result=engine.store.all();break;
 default:throw new Error('Unsupported memory operation');
}
process.stdout.write(JSON.stringify(result));
