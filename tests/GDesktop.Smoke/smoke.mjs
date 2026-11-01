import {spawn,execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {mkdir,readFile,writeFile,copyFile,rename,unlink,stat} from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import http from 'node:http';
import {writeTestPdf} from './pdf-fixtures.mjs';
const workspace=path.resolve(import.meta.dirname,'../..');
const work=path.join(workspace,'tmp','smoke-'+Date.now());
const fixtures=path.join(work,'files'),data=path.join(work,'index'),ready=path.join(work,'ready.txt');
await mkdir(fixtures,{recursive:true});
await writeFile(path.join(fixtures,'relatório.txt'),'Pagamento de serviço. Comprovante de transferência PIX. Referência 938174. <script>alert(1)</script>');
await writeFile(path.join(fixtures,'broken.pdf'),'This is not a PDF');
await writeTestPdf(path.join(fixtures,'arquitetura.pdf'));
await mkdir(path.join(fixtures,'sub'),{recursive:true});
await writeFile(path.join(fixtures,'sub','nota.md'),'Reunião de revisão de arquitetura.');
let child,base,cookie;
const logs=[];
const wait=ms=>new Promise(r=>setTimeout(r,ms));
async function until(fn,label,timeout=60000){const end=Date.now()+timeout;let last;while(Date.now()<end){try{const v=await fn();if(v)return v;}catch(e){last=e;}await wait(200);}throw new Error('Timeout: '+label+' '+(last?.message||''));}
async function start(){
  try{await unlink(ready);}catch{}
  const published=!!process.env.SMOKE_PUBLISHED;
  child=spawn(path.join(workspace,published?(process.env.SMOKE_APP||'artifacts/GDesktop/GDesktop.exe'):'.dotnet/dotnet.exe'),[...(published?[]:[path.join(workspace,'src/GDesktop/bin/Release/net10.0-windows10.0.19041.0/GDesktop.dll')]),'--headless','--data',data,'--ready-file',ready],{cwd:path.join(workspace,'src/GDesktop'),windowsHide:true,env:{...process.env,DOTNET_ROOT:path.join(workspace,'.dotnet'),DOTNET_CLI_TELEMETRY_OPTOUT:'1'}});
  child.stdout.on('data',b=>logs.push(b.toString()));child.stderr.on('data',b=>logs.push(b.toString()));
  child.stdout.on('data',b=>{if(process.env.SMOKE_VERBOSE)process.stdout.write(b);});
  const url=await until(async()=>{if(child.exitCode!==null)throw new Error('Process exited '+child.exitCode);return await readFile(ready,'utf8');},'application startup');
  const u=new URL(url);base=u.origin;const code=new URLSearchParams(u.hash.slice(1)).get('code');
  let r=await fetch(base+'/api/status');assert.equal(r.status,401,'anonymous API must be denied');
  r=await fetch(base+'/api/session',{method:'POST',headers:{'Content-Type':'application/json',Origin:base},body:JSON.stringify({code})});assert.equal(r.status,200,'login');cookie=r.headers.get('set-cookie').split(';')[0];
  r=await fetch(base+'/api/session',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({code})});assert.equal(r.status,401,'one time login code');
}
async function request(route,body){let r=await fetch(base+'/api/'+route,{signal:AbortSignal.timeout(10000),headers:{Cookie:cookie,'Content-Type':'application/json','X-GDesktop':'1',Origin:base},...(body===undefined?{}:{method:'POST',body:JSON.stringify(body)})});const text=await r.text();assert.equal(r.status,200,route+': '+text);return text?JSON.parse(text):null;}
async function search(q){return request('search?'+new URLSearchParams({q}));}
async function idle(){return until(async()=>{const s=await request('status');return !s.scanning&&!s.catalog.counts.pending&&!s.catalog.counts.processing&&s;},'indexing complete');}
async function stop(){if(child&&child.exitCode===null){child.kill();await new Promise(r=>child.once('exit',r));}}
try{
  await start();
  console.log('Started; checking access controls.');
  let r=await fetch(base+'/api/status',{headers:{Cookie:cookie,Origin:'https://evil.example'}});assert.equal(r.status,403,'foreign origin');
  r=await fetch(base+'/api/pause',{method:'POST',headers:{Cookie:cookie}});assert.equal(r.status,403,'CSRF');
  const badHost=await new Promise((resolve,reject)=>{const req=http.get(base+'/api/status',{headers:{Host:'evil.example',Cookie:cookie}},res=>{res.resume();resolve(res.statusCode);});req.on('error',reject);});assert.equal(badHost,400,'host validation');
  await request('pause',{});await request('roots',{path:fixtures});await wait(700);assert.equal((await request('status')).catalog.paused,true);
  await request('resume',{});await until(async()=>Object.values((await request('status')).catalog.counts).reduce((a,b)=>a+b,0)===4,'discovery');let s=await idle();assert.equal(s.catalog.counts.error,1,'corrupt PDF isolated');
  console.log('Discovery and extraction passed.');
  let hits=await search('relatorio');assert.ok(hits.some(h=>h.name==='relatório.txt'),'accent folding');
  hits=await search('transferencia');assert.ok(hits.some(h=>h.name==='relatório.txt'&&h.snippet.includes('\x01')),'highlight markers');
  hits=await search('segurança');assert.ok(hits.some(h=>h.name==='arquitetura.pdf'),'PDF content');
  assert.equal((await search('938174')).length,1,'numeric reference');
  assert.equal((await request('search?'+new URLSearchParams({q:'',ext:'.md'}))).length,1,'extension filter');
  const malformed=await fetch(base+'/api/search?'+new URLSearchParams({q:'" OR * ()'}),{headers:{Cookie:cookie,Origin:base}});assert.equal(malformed.status,400,'malformed search gives friendly validation');
  const availabilityFixture=path.join(fixtures,'relatório.txt'),originalStat=await stat(availabilityFixture);
  const setAvailability=async offline=>promisify(execFile)('powershell.exe',['-NoProfile','-Command',`[IO.File]::SetAttributes('${availabilityFixture.replaceAll("'","''")}', [IO.FileAttributes]::Archive${offline?' -bor [IO.FileAttributes]::Offline':''})`],{windowsHide:true});
  await setAvailability(true);
  await until(async()=> (await search('relatorio')).some(h=>h.name==='relatório.txt'&&h.state==='online-only'),'attributes-only transition to offline');
  assert.equal((await search('938174')).length,0,'offline content skipped');
  await setAvailability(false);
  await until(async()=> (await search('938174')).some(h=>h.name==='relatório.txt'&&h.state==='indexed'),'attributes-only transition to local');
  const updatedStat=await stat(availabilityFixture);assert.equal(updatedStat.size,originalStat.size);assert.equal(updatedStat.mtimeMs,originalStat.mtimeMs,'availability changed without content modification');
  console.log('PASS: attribute-only changes trigger reindexing via watcher, without size/mtime changes.');
  console.log('Search passed; checking updates.');
  await writeFile(path.join(fixtures,'relatório.txt'),'Alteração confirmada: jabuticaba.');await request('rescan',{});await until(async()=> (await search('jabuticaba')).length===1,'updated content');assert.equal((await search('938174')).length,0,'old content removed');
  await rename(path.join(fixtures,'sub','nota.md'),path.join(fixtures,'sub','renomeado.md'));await request('rescan',{});await until(async()=>{const h=await search('renomeado');return h.length===1;},'rename');
  await unlink(path.join(fixtures,'relatório.txt'));await request('rescan',{});await until(async()=> (await search('jabuticaba')).length===0,'deletion');
  await idle();await request('pause',{});await stop();await start();assert.equal((await request('status')).catalog.paused,true,'pause persists');assert.ok((await search('segurança')).length,'index survives restart');
  console.log('Restart passed.');
  await writeFile(path.join(fixtures,'offline.txt'),'Mudança com aplicativo fechado: mangabeira.');await request('resume',{});await until(async()=> (await search('mangabeira')).length===1,'startup reconciliation');
  await idle();const rootId=(await request('status')).catalog.roots[0].id;
  await request('pause',{});await request('roots/'+rootId+'/remove',{});
  assert.equal((await request('status')).catalog.roots.length,0,'removed root');assert.equal((await search('')).length,0,'removed documents');assert.equal((await search('segurança')).length,0,'removed FTS content');
  assert.ok((await readFile(path.join(fixtures,'arquitetura.pdf'))).length>0,'original PDF preserved');
  await request('resume',{});await request('rescan',{});await wait(800);assert.equal((await search('')).length,0,'removed root stays removed');
  await request('roots',{path:fixtures});await until(async()=> (await search('segurança')).length>0,'re-add removed root');
  // Remove while extraction may still be active, and ensure late results cannot resurrect index entries.
  const readded=(await request('status')).catalog.roots[0].id;await request('roots/'+readded+'/remove',{});await wait(1200);assert.equal((await search('')).length,0,'no resurrection');
  await stop();await start();assert.equal((await request('status')).catalog.roots.length,0,'root removal persists');
  const tree=path.join(work,'merge-tree'),childA=path.join(tree,'node_modules','explicit'),childB=path.join(tree,'second');
  await mkdir(childA,{recursive:true});await mkdir(childB,{recursive:true});
  await writeFile(path.join(childA,'first.txt'),'pitangueira alfa');await writeFile(path.join(childB,'second.txt'),'pitangueira beta');
  await writeFile(path.join(tree,'new.txt'),'aceroleira documento novo');
  await request('roots',{path:childA});await request('roots',{path:childB});
  await until(async()=> (await search('pitangueira')).filter(h=>h.state==='indexed').length===2,'children indexed');await idle();
  const beforeMerge=await search('pitangueira');
  await request('pause',{});const merge=await request('roots',{path:tree});
  assert.equal(merge.consolidated.length,2);assert.equal(merge.added,true);
  assert.deepEqual((await search('pitangueira')).map(h=>[h.id,h.state]),beforeMerge.map(h=>[h.id,h.state]),'content immediately available while paused; IDs and states preserved');
  assert.equal((await request('status')).catalog.roots.length,1,'children replaced by parent');
  assert.equal((await request('roots',{path:childA})).added,false,'child already covered');
  assert.equal((await request('roots',{path:tree.toUpperCase()})).added,false,'case-insensitive duplicate');
  await request('resume',{});await until(async()=> (await search('aceroleira')).length===1,'parent discovers new content');await idle();
  assert.deepEqual((await search('pitangueira')).map(h=>h.id),beforeMerge.map(h=>h.id),'old document IDs preserved after scan, including explicitly selected excluded subtree');
  assert.equal((await search('')).length,3,'no duplicated documents');
  await stop();await start();await idle();assert.equal((await search('pitangueira')).length,2,'consolidated subtree coverage survives restart');
  await request('roots/'+merge.root.id+'/remove',{});assert.equal((await search('')).length,0,'merged root removal cleans all content');
  console.log('PASS: parent consolidation retains content/IDs, discovers missing files, preserves explicit subtrees, avoids duplicates and survives restart.');
  const booleanFiles=path.join(work,'boolean');await mkdir(booleanFiles);
  for(const [name,text] of [
    ['a.txt','juriti pix conta de luz comprovante de pagamento'],
    ['b.txt','juriti boleto conta de luz pagamento cancelado'],
    ['c.txt','juriti pix conta de luzes pagamento pendente'],
    ['d.txt','juriti transferencia comprovante de agendamento pagamento OR'],
    ['e.txt','juriti comeco '+('enchimento '.repeat(600))+' fim negativo']
  ])await writeFile(path.join(booleanFiles,name),text);
  await request('roots',{path:booleanFiles});await until(async()=> (await search('juriti')).filter(h=>h.state==='indexed').length===5,'boolean fixtures indexed');
  const names=async q=>(await search(q)).map(h=>h.name).sort();
  assert.deepEqual(await names('pix AND boleto'),[]);
  assert.deepEqual(await names('pix OR boleto'),['a.txt','b.txt','c.txt']);
  assert.deepEqual(await names('pix OR boleto AND cancelado'),['a.txt','b.txt','c.txt'],'AND before OR');
  assert.deepEqual(await names('(pix OR boleto) AND cancelado'),['b.txt']);
  assert.deepEqual(await names('(pix OR boleto) "conta de luz"'),['a.txt','b.txt'],'implicit AND with group and phrase');
  assert.deepEqual(await names('conta de luz'),['a.txt','b.txt','c.txt'],'unquoted words retain prefix search');
  assert.deepEqual(await names('"CONTA DE LUZ"'),['a.txt','b.txt'],'phrase is consecutive, whole tokens and case insensitive');
  assert.deepEqual(await names('"comprovante de pagamento"'),['a.txt']);
  assert.deepEqual(await names('"pagamento de comprovante"'),[],'phrase order');
  assert.deepEqual(await names('juriti NOT cancelado'),['a.txt','c.txt','d.txt','e.txt']);
  assert.deepEqual(await names('juriti AND NOT cancelado'),['a.txt','c.txt','d.txt','e.txt']);
  assert.deepEqual(await names('NOT juriti'),[]);assert.equal((await names('NOT NOT juriti')).length,5);
  assert.deepEqual(await names('comeco AND fim'),['e.txt'],'AND spans chunks');
  assert.deepEqual(await names('comeco NOT negativo'),[],'NOT excludes term in another chunk');
  assert.deepEqual(await names('"comeco fim"'),[],'phrase requires adjacency');
  assert.deepEqual(await names('"OR"'),['d.txt'],'quoted operator is literal');
  assert.deepEqual(await names('"\'); DROP TABLE documents; --"'),[],'quoted SQL remains a search literal');
  for(const q of ['"inacabada','""','pix OR','AND pix','(pix OR boleto','pix)','()','NOT','pix AND OR boleto']){
    const response=await fetch(base+'/api/search?'+new URLSearchParams({q}),{headers:{Cookie:cookie,Origin:base}});
    assert.equal(response.status,400,'syntax validation: '+q);assert.ok((await response.json()).error);
  }
  assert.equal((await names('juriti')).length,5,'catalog intact after invalid input');
  console.log('PASS: boolean operators, precedence, grouping, quoted phrases, literal operators, whole-document AND/NOT, safe literals and syntax errors.');
  console.log('PASS: root removal, FTS cleanup, original files preserved, re-add, in-flight work and restart.');
  console.log('PASS: login, token replay, Host/Origin/CSRF, recursive scan, TXT/PDF, accents, snippets, filters, corrupt PDF, update, rename, deletion, persistent pause and restart.');
  console.log('Test data: '+work);
}catch(e){console.error(logs.slice(-30).join(''));throw e;}finally{await stop();}
