import {spawn,execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {readFile,unlink,stat,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import path from 'node:path';
import assert from 'node:assert/strict';
import {officeFixtures,updatedOfficeWord} from './office-fixtures.mjs';

const workspace=path.resolve(import.meta.dirname,'../..'),work=path.join(workspace,'tmp','office-'+Date.now());
const files=path.join(work,'files'),data=path.join(work,'index'),ready=path.join(work,'ready.txt');
const names=await officeFixtures(files),hashes=new Map(),modified=new Map();
for(const name of names){hashes.set(name,createHash('sha256').update(await readFile(path.join(files,name))).digest('hex'));modified.set(name,(await stat(path.join(files,name))).mtimeMs);}
const setOffline=async offline=>promisify(execFile)('powershell.exe',['-NoProfile','-Command',`[IO.File]::SetAttributes('${path.join(files,'cloud.docx').replaceAll("'","''")}', [IO.FileAttributes]::Archive${offline?' -bor [IO.FileAttributes]::Offline':''})`],{windowsHide:true});
await setOffline(true);
let app,base,cookie;const logs=[];const sleep=ms=>new Promise(r=>setTimeout(r,ms));
async function until(fn,label,timeout=60000){const end=Date.now()+timeout;while(Date.now()<end){const result=await fn();if(result)return result;await sleep(200);}throw Error('Timeout: '+label);}
async function start(executable){
 try{await unlink(ready);}catch{}
 app=spawn(path.resolve(workspace,executable),['--headless','--data',data,'--ready-file',ready],{windowsHide:true,env:{...process.env,DOTNET_ROOT:path.join(workspace,'.dotnet')}});
 app.stdout.on('data',b=>logs.push(b.toString()));app.stderr.on('data',b=>logs.push(b.toString()));
 const url=await until(async()=>{try{return await readFile(ready,'utf8');}catch{return null;}},'startup');const u=new URL(url);base=u.origin;
 const response=await fetch(base+'/api/session',{method:'POST',headers:{'Content-Type':'application/json',Origin:base},body:JSON.stringify({code:new URLSearchParams(u.hash.slice(1)).get('code')})});assert.equal(response.status,200);cookie=response.headers.get('set-cookie').split(';')[0];
}
async function api(route,body){const r=await fetch(base+'/api/'+route,{signal:AbortSignal.timeout(10000),headers:{Cookie:cookie,Origin:base,'Content-Type':'application/json','X-GDesktop':'1'},...(body===undefined?{}:{method:'POST',body:JSON.stringify(body)})});const text=await r.text();assert.equal(r.status,200,route+': '+text);return text?JSON.parse(text):null;}
async function search(q,ext){return api('search?'+new URLSearchParams({q,...(ext?{ext}:{})}));}
async function stop(){if(app&&app.exitCode===null){const exited=new Promise(r=>app.once('exit',r));app.kill();await exited;}}
async function idle(){return until(async()=>{const s=await api('status'),c=s.catalog.counts;return !s.scanning&&!c.pending&&!c.processing&&Object.values(c).reduce((a,b)=>a+b,0)===names.length;},'index complete');}
try{
 // The currently installed pre-Office build creates a genuine old metadata-only index.
 if(process.env.OFFICE_OLD_APP){
  await start(process.env.OFFICE_OLD_APP);await api('roots',{path:files});await idle();
  assert.equal((await search('word.docx')).find(h=>h.name==='word.docx').state,'metadata');
  await api('pause',{});await stop();
 }
 await start(process.env.OFFICE_APP||'artifacts/GDesktop-next/GDesktop.exe');
 assert.equal((await api('status')).version,'1.0.0');
 if(!process.env.OFFICE_OLD_APP)await api('roots',{path:files});
 await api('resume',{});await idle();
 const all=await search(''),byName=new Map(all.map(h=>[h.name,h]));
 for(const name of ['word.docx','book.xlsx','deck.pptx'])assert.equal(byName.get(name).state,'indexed',name+': '+byName.get(name).error);
 assert.equal(byName.get('cloud.docx').state,'online-only');
 assert.equal(byName.get('legacy.doc').state,'legacy-office');
 assert.equal(byName.get('~$word.docx').state,'metadata');
 for(const name of ['broken.docx','password.docx'])assert.equal(byName.get(name).state,'error',name);
 assert.equal(byName.get('large.docx').state,'truncated');
 for(const term of ['"comprovante de pagamento"','palmeira','jabuticabeira','pitombeira','mangabeira','gravioleira','aceroleira']){
  assert.deepEqual((await search(term)).map(h=>h.name),['word.docx'],'DOCX text, tables, headers, footers and notes: '+term);
 }
 for(const term of ['cajueiro','"comprovante de planilha"','"aluguel condominio"','123.45','150','true','31/10/2026','formulacache','coqueiro','SUM']){
  assert.deepEqual((await search(term)).map(h=>h.name),['book.xlsx'],'XLSX values, shared/inline strings, dates, formula cache and sheets: '+term);
 }
 for(const term of ['"comprovante de apresentacao"','buritizeiro','umbuzeiro'])assert.deepEqual((await search(term)).map(h=>h.name),['deck.pptx'],'PPTX slides and notes: '+term);
 assert.deepEqual((await search('buritizeiro AND umbuzeiro')).map(h=>h.name),['deck.pptx'],'AND across slides/notes');
 assert.deepEqual(await search('buritizeiro NOT umbuzeiro'),[],'NOT across slides/notes');
 assert.deepEqual((await search('comprovante','.xlsx')).map(h=>h.name),['book.xlsx'],'Office extension filter');
 assert.equal((await search('naodeveextrair')).length,0,'legacy content not read');
 await setOffline(false);await until(async()=> (await search('ipescarlate')).some(h=>h.name==='cloud.docx'&&h.state==='indexed'),'Office file becomes locally available');
 for(const name of names){assert.equal(createHash('sha256').update(await readFile(path.join(files,name))).digest('hex'),hashes.get(name),'read-only: '+name);assert.equal((await stat(path.join(files,name))).mtimeMs,modified.get(name),'mtime unchanged: '+name);}
 await writeFile(path.join(files,'word.docx'),updatedOfficeWord());
 await until(async()=> (await search('alteracaoword')).some(h=>h.name==='word.docx'&&h.state==='indexed'),'Office content update');
 assert.ok(!(await search('ipescarlate')).some(h=>h.name==='word.docx'),'old Office body removed');
 await stop();await start(process.env.OFFICE_APP||'artifacts/GDesktop-next/GDesktop.exe');await idle();
 assert.ok((await search('umbuzeiro')).some(h=>h.state==='indexed'),'Office index survives restart');
 assert.ok((await search('alteracaoword')).some(h=>h.name==='word.docx'),'Office update survives restart');
 console.log('PASS: Office v1.0, DOCX rich runs/tables/headers/footers/notes/comments; XLSX shared/inline strings, numbers/dates, cached formulas, hidden sheets; PPTX slides/notes; boolean queries; cloud guard and local transition; corrupt/encrypted/legacy/lock files; truncation; read-only hashes; restart'+(process.env.OFFICE_OLD_APP?'; old index automatic upgrade.':'.'));
 console.log('Test data: '+work);
}catch(e){console.error(logs.slice(-15).join(''));throw e;}finally{await stop();}
