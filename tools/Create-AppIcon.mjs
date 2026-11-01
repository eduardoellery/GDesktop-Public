// Rebuild the Windows icon from the existing favicon; requires the optional UI-test tools.
import {chromium} from '../tmp/browser/node_modules/playwright/index.mjs';
import {readFile,mkdir,writeFile} from 'node:fs/promises';
import path from 'node:path';
const root=path.resolve(import.meta.dirname,'..');
const svg=await readFile(path.join(root,'src/GDesktop/wwwroot/favicon.svg'),'utf8');
const browser=await chromium.launch({executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',headless:true});
try{
 const sizes=[16,24,32,48,64,128,256],images=[];
 for(const size of sizes){
  const page=await browser.newPage({viewport:{width:size,height:size},deviceScaleFactor:1});
  await page.setContent(`<style>body{margin:0}svg{display:block;width:100vw;height:100vh}</style>${svg}`);
  images.push(await page.screenshot({omitBackground:true}));await page.close();
 }
 const header=Buffer.alloc(6+16*sizes.length);header.writeUInt16LE(1,2);header.writeUInt16LE(sizes.length,4);
 let offset=header.length;
 for(let i=0;i<sizes.length;i++){
  const entry=6+i*16;header[entry]=header[entry+1]=sizes[i]===256?0:sizes[i];
  header.writeUInt16LE(1,entry+4);header.writeUInt16LE(32,entry+6);
  header.writeUInt32LE(images[i].length,entry+8);header.writeUInt32LE(offset,entry+12);offset+=images[i].length;
 }
 await mkdir(path.join(root,'src/GDesktop/Assets'),{recursive:true});
 await writeFile(path.join(root,'src/GDesktop/Assets/GDesktop.ico'),Buffer.concat([header,...images]));
 await mkdir(path.join(root,'tmp/icon'),{recursive:true});
 await writeFile(path.join(root,'tmp/icon/GDesktop.png'),images.at(-1));
 console.log('GDesktop.ico: 16, 24, 32, 48, 64, 128 e 256 pixels.');
}finally{await browser.close();}
