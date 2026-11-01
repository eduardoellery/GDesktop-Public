import {spawn,execFileSync} from 'node:child_process';
import {mkdir,readFile,copyFile,unlink} from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
const root=path.resolve(import.meta.dirname,'../..'),work=path.join(root,'tmp','ocr-integration-'+Date.now()),files=path.join(work,'files'),ready=path.join(work,'ready.txt');
await mkdir(files,{recursive:true});
try{await readFile(path.join(root,'tmp/windows-ocr-published/digitalizado.pdf'));}
catch{
 execFileSync(path.join(root,'.dotnet/dotnet.exe'),['run','--project',path.join(root,'experiments/WindowsOcr/WindowsOcr.csproj'),'-c','Release','--','--self-test',path.join(root,'tmp/windows-ocr-published')],{cwd:root,windowsHide:true,stdio:'inherit',env:{...process.env,DOTNET_CLI_HOME:path.join(root,'.cli'),NUGET_PACKAGES:path.join(root,'.nuget')}});
}
for(const name of ['comprovante.png','digitalizado.pdf'])await copyFile(path.join(root,'tmp/windows-ocr-published',name),path.join(files,name));
await copyFile(path.join(files,'digitalizado.pdf'),path.join(files,'cloud.pdf'));
const hash=async name=>createHash('sha256').update(await readFile(path.join(files,name))).digest('hex');
const original=await hash('digitalizado.pdf');
function offline(value){execFileSync('powershell.exe',['-NoProfile','-Command',`[IO.File]::SetAttributes('${path.join(files,'cloud.pdf')}',[IO.FileAttributes]::Archive${value?' -bor [IO.FileAttributes]::Offline':''})`],{windowsHide:true});}
offline(true);
let app,base,cookie;const logs=[];
async function until(fn,label){for(let i=0;i<600;i++){if(await fn())return;await new Promise(r=>setTimeout(r,100));}throw Error('Timeout '+label);}
async function api(route,body){const r=await fetch(base+'/api/'+route,{headers:{Cookie:cookie,Origin:base,'Content-Type':'application/json','X-GDesktop':'1'},...(body===undefined?{}:{method:'POST',body:JSON.stringify(body)})});assert.equal(r.status,200,await r.clone().text());const text=await r.text();return text?JSON.parse(text):null;}
const search=q=>api('search?'+new URLSearchParams({q}));
async function start(){try{await unlink(ready);}catch{}app=spawn(path.join(root,process.env.OCR_APP||'artifacts/GDesktop-next/GDesktop.exe'),['--headless','--data',path.join(work,'index'),'--ready-file',ready],{windowsHide:true});app.stderr.on('data',b=>logs.push(b.toString()));await until(async()=>{try{const u=new URL(await readFile(ready,'utf8'));base=u.origin;const r=await fetch(base+'/api/session',{method:'POST',headers:{'Content-Type':'application/json',Origin:base},body:JSON.stringify({code:new URLSearchParams(u.hash.slice(1)).get('code')})});cookie=r.headers.get('set-cookie').split(';')[0];return true;}catch{return false;}},'startup');}
async function stop(){if(app?.exitCode===null){const done=new Promise(r=>app.once('exit',r));app.kill();await done;}}
try{
 await start();await api('roots',{path:files});
 await until(async()=>{const hits=await search('OCRTESTE9371');return ['comprovante.png','digitalizado.pdf'].every(n=>hits.some(h=>h.name===n&&h.state==='indexed-ocr'));},'image and scanned PDF OCR');
 assert.deepEqual((await search('OCRTESTE9371 AND PIXTESTE5328')).map(h=>h.name),['digitalizado.pdf']);
 assert.equal((await search('cloud.pdf'))[0].state,'online-only');
 await api('pause',{});await stop();await start();assert.equal((await api('status')).catalog.paused,true);
 assert.ok((await search('PIXTESTE5328')).some(h=>h.name==='digitalizado.pdf'));
 offline(false);await api('resume',{});await api('rescan',{});
 await until(async()=>(await search('PIXTESTE5328')).some(h=>h.name==='cloud.pdf'&&h.state==='indexed-ocr'),'cloud becomes local');
 assert.equal(await hash('digitalizado.pdf'),original);
 console.log('PASS: Windows OCR image and both PDF pages, cross-page boolean search, cloud guard/local transition, restart, pause persistence, originals unchanged.');
}catch(e){console.error(logs.join(''));throw e;}finally{offline(false);await stop();}

