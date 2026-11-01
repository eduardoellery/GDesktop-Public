import {writeFile,mkdir} from 'node:fs/promises';
import {deflateRawSync} from 'node:zlib';
import path from 'node:path';

// Small independent OPC fixtures: stored ZIP central directory + standard Office XML.
// No installed Office application or test-only extraction implementation is involved.
function crc32(bytes){let crc=0xffffffff;for(const byte of bytes){crc^=byte;for(let i=0;i<8;i++)crc=(crc>>>1)^((crc&1)?0xedb88320:0);}return (crc^0xffffffff)>>>0;}
function zip(entries){
 const local=[],central=[];let offset=0;
 for(const [name,value]of Object.entries(entries)){
  const n=Buffer.from(name),data=Buffer.from(value),compressed=deflateRawSync(data),crc=crc32(data);
  const h=Buffer.alloc(30);h.writeUInt32LE(0x04034b50);h.writeUInt16LE(20,4);h.writeUInt16LE(8,8);h.writeUInt32LE(crc,14);h.writeUInt32LE(compressed.length,18);h.writeUInt32LE(data.length,22);h.writeUInt16LE(n.length,26);
  local.push(h,n,compressed);const c=Buffer.alloc(46);c.writeUInt32LE(0x02014b50);c.writeUInt16LE(20,4);c.writeUInt16LE(20,6);c.writeUInt16LE(8,10);c.writeUInt32LE(crc,16);c.writeUInt32LE(compressed.length,20);c.writeUInt32LE(data.length,24);c.writeUInt16LE(n.length,28);c.writeUInt32LE(offset,42);central.push(c,n);offset+=h.length+n.length+compressed.length;
 }
 const cd=Buffer.concat(central),end=Buffer.alloc(22);end.writeUInt32LE(0x06054b50);end.writeUInt16LE(Object.keys(entries).length,8);end.writeUInt16LE(Object.keys(entries).length,10);end.writeUInt32LE(cd.length,12);end.writeUInt32LE(offset,16);return Buffer.concat([...local,cd,end]);
}
const relNs='http://schemas.openxmlformats.org/package/2006/relationships',officeRel='http://schemas.openxmlformats.org/officeDocument/2006/relationships/';
const w='http://schemas.openxmlformats.org/wordprocessingml/2006/main',a='http://schemas.openxmlformats.org/drawingml/2006/main',p='http://schemas.openxmlformats.org/presentationml/2006/main',s='http://schemas.openxmlformats.org/spreadsheetml/2006/main';
const rels=items=>`<Relationships xmlns="${relNs}">${items.map(([id,type,target])=>`<Relationship Id="${id}" Type="${officeRel+type}" Target="${target}"/>`).join('')}</Relationships>`;
const types=items=>`<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>${items.map(([part,type])=>`<Override PartName="/${part}" ContentType="application/vnd.openxmlformats-officedocument.${type}"/>`).join('')}</Types>`;
const wp=text=>`<w:p><w:r><w:t xml:space="preserve">${text}</w:t></w:r></w:p>`;
function word(main){return zip({
 '[Content_Types].xml':types([['word/document.xml','wordprocessingml.document.main+xml'],['word/header1.xml','wordprocessingml.header+xml'],['word/footer1.xml','wordprocessingml.footer+xml'],['word/footnotes.xml','wordprocessingml.footnotes+xml'],['word/endnotes.xml','wordprocessingml.endnotes+xml'],['word/comments.xml','wordprocessingml.comments+xml']]),
 '_rels/.rels':rels([['rId1','officeDocument','word/document.xml']]),
 'word/_rels/document.xml.rels':rels([['rIdHeader','header','header1.xml'],['rIdFooter','footer','footer1.xml'],['rIdFoot','footnotes','footnotes.xml'],['rIdEnd','endnotes','endnotes.xml'],['rIdComments','comments','comments.xml']]),
 'word/document.xml':`<w:document xmlns:w="${w}" xmlns:r="${officeRel.slice(0,-1)}"><w:body>${main}<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w="2400"/></w:tblGrid><w:tr><w:tc>${wp('palmeira tabela contrato')}</w:tc></w:tr></w:tbl><w:sectPr><w:headerReference w:type="default" r:id="rIdHeader"/><w:footerReference w:type="default" r:id="rIdFooter"/></w:sectPr></w:body></w:document>`,
 'word/header1.xml':`<w:hdr xmlns:w="${w}">${wp('cabecalho jabuticabeira')}</w:hdr>`,
 'word/footer1.xml':`<w:ftr xmlns:w="${w}">${wp('rodape pitombeira')}</w:ftr>`,
 'word/footnotes.xml':`<w:footnotes xmlns:w="${w}"><w:footnote w:id="1">${wp('notarodape mangabeira')}</w:footnote></w:footnotes>`,
 'word/endnotes.xml':`<w:endnotes xmlns:w="${w}"><w:endnote w:id="1">${wp('notafinal gravioleira')}</w:endnote></w:endnotes>`,
 'word/comments.xml':`<w:comments xmlns:w="${w}"><w:comment w:id="0" w:author="Teste">${wp('comentarioword aceroleira')}</w:comment></w:comments>`
});}
function excel(){const serial=(Date.UTC(2026,9,31)-Date.UTC(1899,11,30))/86400000;return zip({
 '[Content_Types].xml':types([['xl/workbook.xml','spreadsheetml.sheet.main+xml'],['xl/worksheets/sheet1.xml','spreadsheetml.worksheet+xml'],['xl/worksheets/sheet2.xml','spreadsheetml.worksheet+xml'],['xl/sharedStrings.xml','spreadsheetml.sharedStrings+xml'],['xl/styles.xml','spreadsheetml.styles+xml']]),
 '_rels/.rels':rels([['rId1','officeDocument','xl/workbook.xml']]),
 'xl/_rels/workbook.xml.rels':rels([['rId1','worksheet','worksheets/sheet1.xml'],['rId2','worksheet','worksheets/sheet2.xml'],['rId3','sharedStrings','sharedStrings.xml'],['rId4','styles','styles.xml']]),
 'xl/workbook.xml':`<workbook xmlns="${s}" xmlns:r="${officeRel.slice(0,-1)}"><sheets><sheet name="Recebimentos" sheetId="1" r:id="rId1"/><sheet name="Oculta" sheetId="2" state="hidden" r:id="rId2"/></sheets></workbook>`,
 'xl/sharedStrings.xml':`<sst xmlns="${s}" count="2" uniqueCount="2"><si><t>cajueiro</t></si><si><r><t xml:space="preserve">comprovante de </t></r><r><t>planilha</t></r></si></sst>`,
 'xl/styles.xml':`<styleSheet xmlns="${s}"><fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="14" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs></styleSheet>`,
 'xl/worksheets/sheet1.xml':`<worksheet xmlns="${s}"><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row><row r="2"><c r="A2" t="inlineStr"><is><r><t xml:space="preserve">aluguel </t></r><r><t>condominio</t></r></is></c><c r="B2"><v>123.45</v></c><c r="C2" t="b"><v>1</v></c></row><row r="3"><c r="A3"><f>SUM(B2,26.55)</f><v>150</v></c><c r="B3" s="1"><v>${serial}</v></c><c r="C3" t="str"><f>IF(1=1,"formulacache","")</f><v>formulacache</v></c></row></sheetData></worksheet>`,
 'xl/worksheets/sheet2.xml':`<worksheet xmlns="${s}"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>folhaoculta coqueiro</t></is></c></row></sheetData></worksheet>`
});}
const shape=(body)=>`<p:sp><p:nvSpPr><p:cNvPr id="2" name="Text"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/>${body}</p:txBody></p:sp>`;
const slide=(text)=>`<p:sld xmlns:p="${p}" xmlns:a="${a}"><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>${shape(text)}</p:spTree></p:cSld></p:sld>`;
function powerpoint(){return zip({
 '[Content_Types].xml':types([['ppt/presentation.xml','presentationml.presentation.main+xml'],['ppt/slides/slide1.xml','presentationml.slide+xml'],['ppt/slides/slide2.xml','presentationml.slide+xml'],['ppt/notesSlides/notesSlide1.xml','presentationml.notesSlide+xml']]),
 '_rels/.rels':rels([['rId1','officeDocument','ppt/presentation.xml']]),
 'ppt/_rels/presentation.xml.rels':rels([['rId1','slide','slides/slide1.xml'],['rId2','slide','slides/slide2.xml']]),
 'ppt/presentation.xml':`<p:presentation xmlns:p="${p}" xmlns:r="${officeRel.slice(0,-1)}"><p:sldIdLst><p:sldId id="256" r:id="rId2"/><p:sldId id="257" r:id="rId1"/></p:sldIdLst><p:sldSz cx="9144000" cy="6858000"/><p:notesSz cx="6858000" cy="9144000"/></p:presentation>`,
 'ppt/slides/slide1.xml':slide('<a:p><a:r><a:t>comprovante de </a:t></a:r><a:r><a:t>apresentacao</a:t></a:r></a:p>'),
 'ppt/slides/slide2.xml':slide('<a:p><a:r><a:t>buritizeiro proposta comercial</a:t></a:r></a:p>'),
 'ppt/slides/_rels/slide1.xml.rels':rels([['rIdNotes','notesSlide','../notesSlides/notesSlide1.xml']]),
 'ppt/notesSlides/notesSlide1.xml':`<p:notes xmlns:p="${p}" xmlns:a="${a}"><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>${shape('<a:p><a:r><a:t>notaapresentador umbuzeiro</a:t></a:r></a:p>')}</p:spTree></p:cSld></p:notes>`
});}
export function updatedOfficeWord(){return word(wp('alteracaoword comprovante atualizado'));}
export async function officeFixtures(folder){
 await mkdir(folder,{recursive:true});
 const doc=word('<w:p><w:r><w:t>compro</w:t></w:r><w:r><w:t>vante de pagamento</w:t></w:r></w:p>'+wp('ipescarlate contrato mensal'));
 const files={ 'word.docx':doc,'book.xlsx':excel(),'deck.pptx':powerpoint(),'cloud.docx':doc,'broken.docx':Buffer.from('not a zip'),'password.docx':Buffer.from([0xd0,0xcf,0x11,0xe0,0xa1,0xb1,0x1a,0xe1]),'large.docx':word(wp('limitetexto '.repeat(185000))),'~$word.docx':Buffer.from('Office lock file'),'legacy.doc':Buffer.from('conteudoantigo naodeveextrair') };
 for(const [name,bytes]of Object.entries(files))await writeFile(path.join(folder,name),bytes);
 return Object.keys(files);
}
