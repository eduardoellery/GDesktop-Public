import {readFile,writeFile} from 'node:fs/promises';
const report=JSON.parse(await readFile('tmp/ocr-compare/results.json','utf8'));
const reference=JSON.parse(await readFile('tmp/ocr-compare/reference.json','utf8'));
const normalize=text=>text.normalize('NFKC').toLowerCase().replace(/\s+/g,' ').trim();
const words=text=>normalize(text).match(/[\p{L}\p{N}]+/gu)||[];
function distance(a,b){let previous=Array.from({length:b.length+1},(_,i)=>i);for(let i=1;i<=a.length;i++){const current=[i];for(let j=1;j<=b.length;j++)current[j]=Math.min(current[j-1]+1,previous[j]+1,previous[j-1]+(a[i-1]===b[j-1]?0:1));previous=current;}return previous[b.length];}
const metrics=[];
for(const config of report.configs)for(const set of ['strata-200','strata-300']){
 const pages=report.results.filter(r=>r.Engine===config.Engine&&r.Model===config.Model&&r.Language===config.Language&&r.Set===set);
 if(!pages.length)continue;
 const body=[];
 for(const page of [1,4]){
  const result=pages.find(r=>r.Page===page),text=normalize(result.Text),end=normalize(reference['page'+page+'End']),index=text.indexOf(end);
  const expected=words(reference['page'+page+'Prefix']);
  body.push({page,words:expected.length,errors:index<0?null:distance(expected,words(text.slice(0,index+end.length)))});
 }
 const table=pages.find(r=>r.Page===4).Text.replace(/,/g,'');
 const values=reference.page4Amounts.map(value=>({value,found:new RegExp('(?<![0-9])'+value.replace(/,/g,'').replace('.','\\.')+'(?![0-9])').test(table)}));
 metrics.push({...config,set,totalMs:pages.reduce((a,r)=>a+r.Milliseconds,0),body,amountsFound:values.filter(v=>v.found).length,amountsTotal:values.length,values});
}
await writeFile('tmp/ocr-compare/metrics.json',JSON.stringify({method:'Manual prefixes from rendered pages 1 and 4; word edit distance ignoring case/punctuation. Ten visually checked monetary values on page 4; literal presence anywhere on page, ignoring comma thousands separators. Not full-document accuracy or table reconstruction.',metrics},null,2));
console.log(JSON.stringify(metrics.map(({Engine,Model,Language,set,totalMs,body,amountsFound,amountsTotal})=>({Engine,Model,Language,set,totalMs,body,amountsFound,amountsTotal})),null,2));
