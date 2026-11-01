// Render the bundled README using DOM nodes; Markdown cannot inject HTML or scripts.
function readmeInline(parent,text){
 const pattern=/(`[^`]+`|\*\*[^*]+\*\*|\[([^\]]+)\]\(([^)]+)\))/g;
 let start=0;
 for(const match of text.matchAll(pattern)){
  parent.append(document.createTextNode(text.slice(start,match.index)));
  const token=match[0];let node;
  if(token.startsWith('`')){node=document.createElement('code');node.textContent=token.slice(1,-1);}
  else if(token.startsWith('**')){node=document.createElement('strong');node.textContent=token.slice(2,-2);}
  else{
   node=document.createElement('span');node.textContent=match[2];
   try{const url=new URL(match[3]);if(['https:','http:'].includes(url.protocol)){node=document.createElement('a');node.textContent=match[2];node.href=url.href;node.target='_blank';node.rel='noopener noreferrer';}}catch{}
  }
  parent.append(node);start=match.index+token.length;
 }
 parent.append(document.createTextNode(text.slice(start)));
}
function renderReadme(markdown){
 const fragment=document.createDocumentFragment(),lines=markdown.replace(/\r/g,'').split('\n');
 let list=null;
 for(let i=0;i<lines.length;i++){
  const line=lines[i];
  if(!line.trim()){list=null;continue;}
  if(line.startsWith('```')){
   list=null;const pre=document.createElement('pre'),code=document.createElement('code'),body=[];
   while(++i<lines.length&&!lines[i].startsWith('```'))body.push(lines[i]);
   code.textContent=body.join('\n');pre.append(code);fragment.append(pre);continue;
  }
  if(line.startsWith('|')&&/^\|[\s:|\-]+\|$/.test(lines[i+1]||'')){
   list=null;const wrapper=document.createElement('div'),table=document.createElement('table');wrapper.className='readme-table';
   const row=(text,header)=>{const tr=document.createElement('tr');for(const cell of text.trim().slice(1,-1).split('|')){const td=document.createElement(header?'th':'td');readmeInline(td,cell.trim());tr.append(td);}table.append(tr);};
   row(line,true);i++;
   while(i+1<lines.length&&lines[i+1].startsWith('|'))row(lines[++i],false);
   wrapper.append(table);fragment.append(wrapper);continue;
  }
  const heading=/^(#{1,6})\s+(.+)$/.exec(line),item=/^(?:([-*])|\d+\.)\s+(.+)$/.exec(line);
  if(item){
   const tag=item[1]?'ul':'ol';if(!list||list.localName!==tag){list=document.createElement(tag);fragment.append(list);}
   const li=document.createElement('li');readmeInline(li,item[2]);list.append(li);continue;
  }
  list=null;const node=document.createElement(heading?'h'+heading[1].length:'p');readmeInline(node,heading?heading[2]:line);fragment.append(node);
 }
 return fragment;
}
const readmeDialog=document.getElementById('readme-dialog'),readmeContent=document.getElementById('readme-content');
document.getElementById('readme-link').addEventListener('click',async event=>{
 event.preventDefault();readmeContent.textContent='Carregando README…';readmeDialog.showModal();
 try{const result=await api('readme');readmeContent.replaceChildren(renderReadme(result.text));readmeContent.scrollTop=0;}
 catch(error){readmeContent.textContent=error.message;}
});
document.getElementById('readme-close').addEventListener('click',()=>readmeDialog.close());
readmeDialog.addEventListener('click',event=>{if(event.target===readmeDialog){const r=readmeDialog.getBoundingClientRect();if(event.clientX<r.left||event.clientX>r.right||event.clientY<r.top||event.clientY>r.bottom)readmeDialog.close();}});
