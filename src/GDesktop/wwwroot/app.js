const $=id=>document.getElementById(id);
let paused=false,lastRoots='',searchVersion=0,hasRoots=false,activeQuery='';
const labels={'ocr-pending':'Na fila de OCR','indexed-ocr':'Conteúdo indexado com Windows OCR','legacy-office':'Office antigo: somente nome e metadados',indexed:'Conteúdo indexado',metadata:'Somente nome e metadados','needs-ocr':'Aguardando etapa de OCR','online-only':'Somente online','too-large':'Limite de 32 MB',truncated:'Texto parcialmente indexado',error:'Erro de extração',pending:'Na fila',processing:'Processando'};
let noticeTimer;
function notice(text,duration=0){clearTimeout(noticeTimer);$('notice').textContent=text;$('notice').hidden=!text;if(text&&duration>0)noticeTimer=setTimeout(()=>notice(''),duration);}
function rootAddedNotice(result){
 if(!result.added){notice('Esta pasta já está coberta por '+result.root.path+'. O índice existente foi mantido.',5000);return;}
 if(result.consolidated.length){
  notice('Pasta adicionada. As subpastas '+result.consolidated.map(r=>r.path).join(', ')+' foram retiradas apenas da lista, pois estão incluídas na pasta superior. Os resultados existentes continuam disponíveis; arquivos novos ou alterados serão indexados.',5000);
 }else notice('Pasta adicionada à indexação.',5000);
}
async function api(path,body){const options=body===undefined?{}:{method:'POST',headers:{'Content-Type':'application/json','X-GDesktop':'1'},body:JSON.stringify(body)};const r=await fetch('/api/'+path,options);if(!r.ok){let e;try{e=await r.json();}catch{}throw new Error(e?.error||(r.status===401?'Abra a pesquisa pelo ícone do GDesktop na bandeja.':'Não foi possível concluir a operação.'));}return r.status===204?null:await r.text().then(t=>t?JSON.parse(t):null);}
function el(tag,text,className){const n=document.createElement(tag);if(text!==undefined)n.textContent=text;if(className)n.className=className;return n;}
function highlighted(text){const p=el('p',undefined,'snippet');let active=false;for(const part of text.split(/([\x01\x02])/)){if(part==='\x01')active=true;else if(part==='\x02')active=false;else p.append(active?el('mark',part):document.createTextNode(part));}return p;}
async function status(){const s=await api('status');paused=s.catalog.paused;$('pause').textContent=paused?'Retomar':'Pausar';const counts=s.catalog.counts,total=Object.values(counts).reduce((a,b)=>a+b,0);$('activity').textContent=paused?'Indexação pausada':s.scanning?'Descobrindo arquivos…':counts.processing?'Extraindo conteúdo…':counts['ocr-pending']?'Windows OCR em andamento':counts.pending?'Indexação em andamento':'Índice atualizado';$('activity-dot').style.background=paused?'#b7a269':'#168777';$('current').textContent=s.current||'Verificação automática de alterações a cada 5 minutos e ao detectar mudanças.';$('counts').replaceChildren();for(const [label,value] of [['arquivos',total],['com texto',(counts.indexed||0)+(counts.truncated||0)+(counts['indexed-ocr']||0)],['na fila',(counts.pending||0)+(counts.processing||0)],['para OCR',(counts['needs-ocr']||0)+(counts['ocr-pending']||0)],['com erro',counts.error||0]]){const item=el('span');item.append(el('strong',value),document.createTextNode(label));$('counts').append(item);}$('messages').replaceChildren(...s.messages.map(t=>el('p',t)));$('data-path').textContent='Índice: '+s.catalog.dataDirectory;hasRoots=s.catalog.roots.length>0;const roots=JSON.stringify(s.catalog.roots);if(roots!==lastRoots){lastRoots=roots;const selected=$('root-filter').value;$('roots').replaceChildren();$('root-filter').replaceChildren(new Option('Todas as pastas','0'));for(const root of s.catalog.roots){const row=el('div',undefined,'root');const heading=el('div',undefined,'root-heading');const remove=el('button','Remover','remove-root');remove.type='button';remove.setAttribute('aria-label','Remover pasta '+root.path);remove.title='Retira a pasta e seus resultados do índice. Os arquivos originais são preservados.';remove.addEventListener('click',()=>run(async()=>{remove.disabled=true;try{await api('roots/'+root.id+'/remove',{});await status();await search();notice('Pasta removida do índice. Os arquivos originais foram preservados.',5000);}finally{remove.disabled=false;}}));heading.append(el('strong',root.path.split(/[\\/]/).filter(Boolean).at(-1)||root.path),remove);row.append(heading,el('small',root.path));$('roots').append(row);$('root-filter').add(new Option(root.path,root.id));}if([...$('root-filter').options].some(o=>o.value===selected))$('root-filter').value=selected;}}
async function search(){const version=++searchVersion;if(!activeQuery){$('search-results').hidden=true;$('results').replaceChildren();$('result-count').textContent='';return;}const params=new URLSearchParams({q:activeQuery,ext:$('extension').value,root:$('root-filter').value});let hits;try{hits=await api('search?'+params);}catch(error){if(version===searchVersion){$('search-results').hidden=true;$('results').replaceChildren();$('result-count').textContent='';}throw error;}if(version!==searchVersion)return;$('search-results').hidden=false;$('result-count').textContent=hits.length+' resultado'+(hits.length===1?'':'s')+(hits.length===100?' (limite da página)':'');if(!hits.length){const box=el('div',undefined,'empty');box.append(el('span','⌕'),el('h3',hasRoots?'Nenhum arquivo encontrado':'Comece escolhendo uma pasta'),el('p',hasRoots?'Tente outras palavras ou aguarde a indexação.':'Use Selecionar pasta para criar seu índice local.'));$('results').replaceChildren(box);return;}$('results').replaceChildren(...hits.map(hit=>{const row=el('article',undefined,'result'),content=el('div',undefined,'result-content'),actions=el('div',undefined,'result-actions');content.append(el('h3',hit.name),el('div',hit.path,'path'));if(hit.snippet)content.append(highlighted(hit.snippet));const meta=el('div',undefined,'result-meta');meta.append(el('span',labels[hit.state]||hit.state),el('span',new Intl.NumberFormat('pt-BR',{maximumFractionDigits:1}).format(hit.size/1024)+' KB'));content.append(meta);if(hit.error)content.append(el('p',hit.error,'path'));for(const [label,action]of [['Abrir','open'],['Mostrar na pasta','reveal']]){const button=el('button',label,action==='reveal'?'secondary':'text-button');button.addEventListener('click',()=>run(()=>api('documents/'+hit.id+'/'+action,{})));actions.append(button);}row.append(el('div',(hit.extension||'ARQ').slice(1).toUpperCase(),'file-icon'),content,actions);return row;}));}
async function run(action,clearNotice=true){if(clearNotice)notice('');try{await action();}catch(e){notice(e.message);}}
const queryInput=$('query');
let pasteRevision=0;
function insertQueryText(text){
 const start=queryInput.selectionStart??queryInput.value.length,end=queryInput.selectionEnd??start;
 // Text copied from a PDF often contains line breaks. Keep words separated in a single-line input.
 const plain=text.replace(/\r\n?|\n/g,' ');
 queryInput.setRangeText(plain,start,end,'end');
 queryInput.focus();queryInput.dispatchEvent(new Event('input',{bubbles:true}));
}
queryInput.addEventListener('paste',event=>{
 if(!event.clipboardData?.types.includes('text/plain'))return;
 const text=event.clipboardData.getData('text/plain');event.preventDefault();pasteRevision++;insertQueryText(text);
});
async function pasteFromClipboard(stillCurrent){
 if(!navigator.clipboard?.readText)throw new Error('Este navegador não oferece acesso à área de transferência. Use Ctrl+V ou clique com o botão direito no campo e escolha Colar.');
 try{const text=await navigator.clipboard.readText();if(text&&(!stillCurrent||stillCurrent())){pasteRevision++;insertQueryText(text);}}
 catch{throw new Error('O navegador não permitiu ler a área de transferência. Use o menu do campo → Colar, ou permita a colagem neste site local.');}
}
$('paste-query').addEventListener('click',()=>run(pasteFromClipboard));
queryInput.addEventListener('keydown',event=>{
 if(!(event.ctrlKey||event.metaKey)||event.altKey||event.key.toLowerCase()!=='v')return;
 // Preserve native Ctrl+V. Only attempt a no-prompt fallback if the paste never arrives
 // and this browser has already granted clipboard access to this local page.
 const revision=pasteRevision,value=queryInput.value;
 setTimeout(async()=>{
  if(pasteRevision!==revision||queryInput.value!==value||document.activeElement!==queryInput)return;
  try{
   const permission=await navigator.permissions?.query({name:'clipboard-read'});
   if(permission?.state==='granted'&&pasteRevision===revision&&queryInput.value===value)await pasteFromClipboard(()=>pasteRevision===revision&&queryInput.value===value&&document.activeElement===queryInput);
  }catch{/* Native paste and the explicit paste button remain available. */}
 },350);
});
$('search-form').addEventListener('submit',e=>{e.preventDefault();activeQuery=$('query').value.trim();run(search);});$('query').addEventListener('input',()=>{if(!$('query').value.trim()){activeQuery='';run(search);}});for(const id of ['extension','root-filter'])$(id).addEventListener('change',()=>run(search));
$('folder-form').addEventListener('submit',e=>{e.preventDefault();run(async()=>{const result=await api('roots',{path:$('folder').value});$('folder').value='';await status();await search();rootAddedNotice(result);});});
$('pick').addEventListener('click',()=>run(async()=>{$('pick').disabled=true;try{const r=await api('pick-folder',{});if(r.path){const result=await api('roots',{path:r.path});await status();await search();rootAddedNotice(result);}}finally{$('pick').disabled=false;}}));
$('pause').addEventListener('click',()=>run(async()=>{await api(paused?'resume':'pause',{});await status();}));$('rescan').addEventListener('click',()=>run(async()=>{await api('rescan',{});await status();}));$('retry').addEventListener('click',()=>run(()=>api('retry',{})));
(async()=>{try{const fragment=new URLSearchParams(location.hash.slice(1)),code=fragment.get('code');history.replaceState(null,'',location.pathname);if(code)await api('session',{code});await status();await search();let ticks=0;setInterval(()=>run(async()=>{await status();if(++ticks%3===0)await search();},false),2000);}catch(e){notice(e.message);}})();
