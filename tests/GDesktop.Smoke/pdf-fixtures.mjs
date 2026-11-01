import {writeFile} from 'node:fs/promises';

// Synthetic PDF with WinAnsi text. No personal document is needed by the tests.
export async function writeTestPdf(file){
 const text=Buffer.from('BT /F1 16 Tf 40 740 Td (Segurança e arquitetura local. Transferência de dados.) Tj ET','latin1');
 const objects=[
  Buffer.from('<< /Type /Catalog /Pages 2 0 R >>'),
  Buffer.from('<< /Type /Pages /Kids [3 0 R] /Count 1 >>'),
  Buffer.from('<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>'),
  Buffer.from('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>'),
  Buffer.concat([Buffer.from(`<< /Length ${text.length} >>\nstream\n`),text,Buffer.from('\nendstream')])
 ];
 const parts=[Buffer.from('%PDF-1.4\n')],offsets=[0];let length=parts[0].length;
 objects.forEach((body,index)=>{
  offsets.push(length);const part=Buffer.concat([Buffer.from(`${index+1} 0 obj\n`),body,Buffer.from('\nendobj\n')]);
  parts.push(part);length+=part.length;
 });
 const xref=length;
 parts.push(Buffer.from(`xref\n0 ${objects.length+1}\n0000000000 65535 f \n${offsets.slice(1).map(n=>String(n).padStart(10,'0')+' 00000 n \n').join('')}trailer\n<< /Size ${objects.length+1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`));
 await writeFile(file,Buffer.concat(parts));
}
