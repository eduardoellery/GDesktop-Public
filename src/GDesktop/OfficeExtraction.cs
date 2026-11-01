using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S=DocumentFormat.OpenXml.Spreadsheet;
using P=DocumentFormat.OpenXml.Presentation;

namespace GDesktop;

internal sealed class OfficeExtraction(int limit)
{
    const string WordNs="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    const string DrawingNs="http://schemas.openxmlformats.org/drawingml/2006/main";
    const string MathNs="http://schemas.openxmlformats.org/officeDocument/2006/math";
    readonly List<ExtractedPage> pages=[];
    int remaining=limit;
    bool truncated;
    static OpenSettings Settings()=>new(){AutoSave=false,MaxCharactersInPart=16_000_000};
    static XmlReader Reader(OpenXmlPart part)=>XmlReader.Create(part.GetStream(FileMode.Open,FileAccess.Read),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=16_000_000,CloseInput=true});

    public static Extracted Extract(string path,string extension,int limit)
    {
        var extractor=new OfficeExtraction(limit);
        try
        {
            switch(extension)
            {
                case ".docx":extractor.Word(path);break;
                case ".xlsx":extractor.Excel(path);break;
                case ".pptx":extractor.PowerPoint(path);break;
                default:throw new ArgumentException("Formato Office não suportado.");
            }
        }
        catch(Exception ex) when(ex is OpenXmlPackageException or InvalidDataException or XmlException or FileFormatException)
        {
            throw new IOException("Não foi possível ler o arquivo Office. Ele pode estar corrompido, protegido por senha ou exceder o limite de XML permitido.",ex);
        }
        return new(extractor.pages,extractor.truncated?"truncated":extractor.pages.Count==0?"metadata":"indexed");
    }
    bool Append(StringBuilder text,string? value)
    {
        if(string.IsNullOrEmpty(value))return true;
        int count=Math.Min(value.Length,remaining-text.Length);
        if(count>0)text.Append(value.AsSpan(0,count));
        if(count<value.Length || text.Length>=remaining){truncated=true;return false;}
        return true;
    }
    void Add(string text,int position)
    {
        if(!string.IsNullOrWhiteSpace(text))pages.Add(new(position,text));
        remaining-=text.Length;
    }
    string TextPart(OpenXmlPart part)
    {
        var text=new StringBuilder();using var reader=Reader(part);
        reader.Read();
        while(!reader.EOF && text.Length<remaining)
        {
            bool textNamespace=reader.NamespaceURI is WordNs or DrawingNs or MathNs;
            if(reader.NodeType==XmlNodeType.Element&&textNamespace&&reader.LocalName=="t")
            {
                if(!Append(text,reader.ReadElementContentAsString()))break;
                continue;
            }
            if(reader.NodeType==XmlNodeType.Element&&reader.NamespaceURI==WordNs)
            {
                if(reader.LocalName=="tab"&&!Append(text,"\t"))break;
                if(reader.LocalName=="br"&&!Append(text,"\n"))break;
            }
            if(reader.NodeType==XmlNodeType.EndElement&&textNamespace&&reader.LocalName=="p"&&!Append(text,"\n"))break;
            reader.Read();
        }
        return text.ToString();
    }
    void Word(string path)
    {
        using var document=WordprocessingDocument.Open(path,false,Settings());
        var main=document.MainDocumentPart??throw new InvalidDataException("Documento sem corpo principal.");
        IEnumerable<OpenXmlPart> parts=new OpenXmlPart[]{main}.Concat(main.HeaderParts).Concat(main.FooterParts);
        if(main.FootnotesPart is not null)parts=parts.Append(main.FootnotesPart);
        if(main.EndnotesPart is not null)parts=parts.Append(main.EndnotesPart);
        if(main.WordprocessingCommentsPart is not null)parts=parts.Append(main.WordprocessingCommentsPart);
        int section=0;
        foreach(var part in parts){if(remaining<=0)break;Add(TextPart(part),++section);}
    }
    void PowerPoint(string path)
    {
        using var document=PresentationDocument.Open(path,false,Settings());
        var presentation=document.PresentationPart??throw new InvalidDataException("Apresentação sem parte principal.");
        var slides=presentation.Presentation?.SlideIdList?.Elements<P.SlideId>()??[];
        int position=0;
        foreach(var id in slides)
        {
            if(remaining<=0)break;position++;
            if(id.RelationshipId?.Value is not string relationship || presentation.GetPartById(relationship) is not SlidePart slide)throw new InvalidDataException("Referência de slide inválida.");
            Add(TextPart(slide),position);
            if(remaining>0&&slide.NotesSlidePart is not null)Add(TextPart(slide.NotesSlidePart),position);
        }
    }
    List<string> SharedStrings(SharedStringTablePart? part)
    {
        var strings=new List<string>();if(part is null)return strings;
        using var reader=OpenXmlReader.Create(part);int characters=0;
        while(reader.Read())
        {
            if(!reader.IsStartElement||reader.ElementType!=typeof(S.SharedStringItem))continue;
            var item=reader.LoadCurrentElement() as S.SharedStringItem??throw new InvalidDataException("Texto compartilhado inválido.");
            string text=string.Concat(item.Descendants<S.Text>().Select(t=>t.Text));
            // Bound auxiliary lookup memory independently of the extracted-text budget.
            if(strings.Count>=100_000||characters+text.Length>8_000_000){truncated=true;break;}
            strings.Add(text);characters+=text.Length;
        }
        return strings;
    }
    static HashSet<uint> DateStyles(WorkbookPart workbook)
    {
        var stylesheet=workbook.WorkbookStylesPart?.Stylesheet;
        var dates=new HashSet<uint>();uint index=0;
        foreach(var format in stylesheet?.CellFormats?.Elements<S.CellFormat>()??[])
        {
            uint id=format.NumberFormatId?.Value??0;
            string? code=stylesheet?.NumberingFormats?.Elements<S.NumberingFormat>().FirstOrDefault(f=>f.NumberFormatId?.Value==id)?.FormatCode?.Value;
            if(code is not null)code=Regex.Replace(code,"\"[^\"]*\"|\\\\.|\\[[^\\]]*\\]","");
            if(id is >=14 and <=22 or >=45 and <=47 || code is not null&&Regex.IsMatch(code,"[ydhs]",RegexOptions.IgnoreCase))dates.Add(index);
            index++;
        }
        return dates;
    }
    void Excel(string path)
    {
        using var document=SpreadsheetDocument.Open(path,false,Settings());
        var workbook=document.WorkbookPart??throw new InvalidDataException("Planilha sem pasta de trabalho.");
        var book=workbook.Workbook??throw new InvalidDataException("Pasta de trabalho sem conteúdo.");
        var shared=SharedStrings(workbook.SharedStringTablePart);
        var dateStyles=DateStyles(workbook);
        bool date1904=book.WorkbookProperties?.Date1904?.Value??false;
        int position=0;
        foreach(var sheet in book.Sheets?.Elements<S.Sheet>()??[])
        {
            if(remaining<=0)break;position++;
            if(sheet.Id?.Value is not string relationship || workbook.GetPartById(relationship) is not WorksheetPart worksheet)continue;
            var text=new StringBuilder();Append(text,sheet.Name?.Value+"\n");
            using var reader=OpenXmlReader.Create(worksheet);
            while(text.Length<remaining&&reader.Read())
            {
                if(reader.IsEndElement&&reader.ElementType==typeof(S.Row)){if(!Append(text,"\n"))break;continue;}
                if(!reader.IsStartElement||reader.ElementType!=typeof(S.Cell))continue;
                var cell=reader.LoadCurrentElement() as S.Cell??throw new InvalidDataException("Célula inválida.");
                string value=cell.CellValue?.Text??"";
                var type=cell.DataType?.Value;
                if(type==S.CellValues.SharedString)
                {
                    if(int.TryParse(value,out int index)&&index>=0&&index<shared.Count)value=shared[index];
                    else{value="";truncated=true;}
                }
                else if(type==S.CellValues.InlineString)value=string.Concat(cell.InlineString?.Descendants<S.Text>().Select(t=>t.Text)??[]);
                else if(type==S.CellValues.Boolean)value=value=="1"?"true":"false";
                else if((type is null||type==S.CellValues.Number)&&dateStyles.Contains(cell.StyleIndex?.Value??0)&&double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out double serial))
                {
                    try
                    {
                        DateTime date=date1904?new DateTime(1904,1,1).AddDays(serial):DateTime.FromOADate(serial);
                        value+=" "+date.ToString("yyyy-MM-dd dd/MM/yyyy HH:mm:ss",CultureInfo.InvariantCulture);
                    }catch(ArgumentException){}
                }
                if(!Append(text,value))break;
                if(cell.CellFormula is not null&&!Append(text," ="+cell.CellFormula.Text))break;
                if(!Append(text,"\t"))break;
            }
            Add(text.ToString(),position);
        }
    }
}
