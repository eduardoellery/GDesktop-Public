using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Windows.Data.Pdf;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace GDesktop.WindowsOcrExperiment;

record PageResult(int Page,int Width,int Height,string Text,long Milliseconds);

internal static class Program
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]
    static extern int GetCurrentPackageFullName(ref uint length,IntPtr name);

    static async Task<int> Main(string[] args)
    {
        try
        {
            uint length=0;bool identity=GetCurrentPackageFullName(ref length,IntPtr.Zero)!=15700;
            var languages=OcrEngine.AvailableRecognizerLanguages.Select(l=>l.LanguageTag).ToArray();
            var engine=OcrEngine.TryCreateFromLanguage(new Language("pt-BR"))??throw new InvalidOperationException("O OCR em português do Brasil não está disponível neste Windows.");
            Console.WriteLine(JsonSerializer.Serialize(new{languages,selected=engine.RecognizerLanguage.LanguageTag,maxDimension=OcrEngine.MaxImageDimension,packageIdentity=identity}));
            if(args.Length==0||args[0]=="--probe")return 0;
            if(args[0]=="--inspect-pdf"&&args.Length>1)
            {
                using var document=UglyToad.PdfPig.PdfDocument.Open(LocalFile(args[1]));
                Console.WriteLine(JsonSerializer.Serialize(new{pages=document.NumberOfPages,textCharacters=document.GetPages().Select(p=>p.Text.Length).ToArray()}));return 0;
            }
            if(args[0]=="--inspect-pdf"&&args.Length>1)
            {
                using var document=UglyToad.PdfPig.PdfDocument.Open(LocalFile(args[1]));
                Console.WriteLine(JsonSerializer.Serialize(new{pages=document.NumberOfPages,textCharacters=document.GetPages().Select(p=>p.Text.Length).ToArray()}));return 0;
            }
            if(args[0]=="--self-test")
            {
                string output=Path.GetFullPath(args.Length>1?args[1]:"tmp/windows-ocr");Directory.CreateDirectory(output);
                string png=Path.Combine(output,"comprovante.png"),pdf=Path.Combine(output,"digitalizado.pdf");
                var images=new List<byte[]>();
                for(int page=1;page<=2;page++)
                {
                    using var bitmap=new Bitmap(1600,1000);using var g=Graphics.FromImage(bitmap);
                    g.Clear(Color.White);g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using var title=new Font("Arial",48,FontStyle.Bold,GraphicsUnit.Pixel);
                    using var body=new Font("Arial",38,FontStyle.Regular,GraphicsUnit.Pixel);
                    g.DrawString(page==1?"COMPROVANTE DE PAGAMENTO":"TRANSFERÊNCIA BANCÁRIA",title,Brushes.Black,80,90);
                    string[] lines=page==1?["Aluguel de outubro","Valor: R$ 1.234,56","Data: 31/10/2026","Referência: OCRTESTE9371","Pagamento confirmado."]:["Comprovante de transferência PIX","Banco de exemplo","Valor: R$ 532,80","Código: PIXTESTE5328","Operação confirmada."];
                    for(int i=0;i<lines.Length;i++)g.DrawString(lines[i],body,Brushes.Black,80,210+90*i);
                    if(page==1)bitmap.Save(png,ImageFormat.Png);
                    using var jpeg=new MemoryStream();bitmap.Save(jpeg,ImageFormat.Jpeg);images.Add(jpeg.ToArray());
                }
                WriteRasterPdf(pdf,images,1600,1000);
                using(var check=UglyToad.PdfPig.PdfDocument.Open(pdf))
                    if(check.GetPages().Any(p=>!string.IsNullOrWhiteSpace(p.Text)))throw new InvalidOperationException("O PDF de teste não deveria ter camada de texto.");
                var imageResult=await Image(png,engine);
                var pdfResults=await Pdf(pdf,engine);
                if(!imageResult.Text.Contains("PAGAMENTO",StringComparison.OrdinalIgnoreCase)||!imageResult.Text.Contains("OCRTESTE9371",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("O OCR da imagem não reconheceu os termos de controle.");
                if(pdfResults.Count!=2||!pdfResults[0].Text.Contains("OCRTESTE9371",StringComparison.OrdinalIgnoreCase)||!pdfResults[1].Text.Contains("PIXTESTE5328",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("O OCR do PDF não reconheceu os termos de controle das duas páginas.");
                var report=new{languages,selected=engine.RecognizerLanguage.LanguageTag,packageIdentity=identity,image=imageResult,pdf=pdfResults,peakMemoryMB=Process.GetCurrentProcess().PeakWorkingSet64/1024/1024};
                await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
                Console.WriteLine(JsonSerializer.Serialize(new{passed=true,pages=pdfResults.Count,imageMs=imageResult.Milliseconds,pdfMs=pdfResults.Sum(p=>p.Milliseconds),output,report.peakMemoryMB}));
                return 0;
            }
            if(args.Length<2)throw new ArgumentException("Use --self-test [pasta], --pdf arquivo.pdf ou --image arquivo.png.");
            double dpi=args.Length>3&&double.TryParse(args[3],out double requestedDpi)?Math.Clamp(requestedDpi,100,400):200;
            var results=args[0]=="--pdf"?await Pdf(args[1],engine,args.Length>2?Path.GetFullPath(args[2]):null,dpi):new List<PageResult>{await Image(args[1],engine)};
            Console.WriteLine(JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex.ToString());return 1;}
    }
    static string LocalFile(string path)
    {
        path=Path.GetFullPath(path);var file=new FileInfo(path);
        if(((int)file.Attributes&(0x1000|0x40000|0x400000))!=0)throw new IOException("Arquivo somente online: o teste não fará download.");
        if(file.Length>32*1024*1024)throw new IOException("Arquivo excede o limite de 32 MB do teste.");return path;
    }
    static async Task<PageResult> Image(string path,OcrEngine engine)
    {
        path=LocalFile(path);var watch=Stopwatch.StartNew();var file=await StorageFile.GetFileFromPathAsync(path);
        using var stream=await file.OpenAsync(FileAccessMode.Read);var decoder=await BitmapDecoder.CreateAsync(stream);
        if(decoder.PixelWidth>OcrEngine.MaxImageDimension||decoder.PixelHeight>OcrEngine.MaxImageDimension)throw new IOException("Imagem excede as dimensões do motor OCR.");
        using var bitmap=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied);
        var result=await engine.RecognizeAsync(bitmap).AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        return new(1,bitmap.PixelWidth,bitmap.PixelHeight,string.Join('\n',result.Lines.Select(l=>l.Text)),watch.ElapsedMilliseconds);
    }
    static async Task<List<PageResult>> Pdf(string path,OcrEngine engine,string? renderOutput=null,double dpi=200)
    {
        path=LocalFile(path);var file=await StorageFile.GetFileFromPathAsync(path);var pdf=await PdfDocument.LoadFromFileAsync(file);
        var pages=new List<PageResult>();
        for(uint i=0;i<Math.Min(pdf.PageCount,5);i++)
        {
            var watch=Stopwatch.StartNew();using var page=pdf.GetPage(i);
            // Higher-resolution comparison is optional; the default remains bounded at 2800px.
            double scale=Math.Min(dpi/96,(dpi>200?4000:2800)/Math.Max(page.Size.Width,page.Size.Height));
            uint width=(uint)Math.Max(1,Math.Round(page.Size.Width*scale)),height=(uint)Math.Max(1,Math.Round(page.Size.Height*scale));
            using var rendered=new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(rendered,new PdfPageRenderOptions{DestinationWidth=width,DestinationHeight=height,BackgroundColor=new Windows.UI.Color{A=255,R=255,G=255,B=255}});
            if(renderOutput is not null)
            {
                Directory.CreateDirectory(renderOutput);
                using var dataReader=new DataReader(rendered.GetInputStreamAt(0));
                await dataReader.LoadAsync((uint)rendered.Size);var bytes=new byte[(int)rendered.Size];dataReader.ReadBytes(bytes);
                await File.WriteAllBytesAsync(Path.Combine(renderOutput,$"page-{i+1}.png"),bytes);
            }
            rendered.Seek(0);var decoder=await BitmapDecoder.CreateAsync(rendered);
            using var bitmap=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied);
            var result=await engine.RecognizeAsync(bitmap).AsTask().WaitAsync(TimeSpan.FromSeconds(30));
            pages.Add(new((int)i+1,bitmap.PixelWidth,bitmap.PixelHeight,string.Join('\n',result.Lines.Select(l=>l.Text)),watch.ElapsedMilliseconds));
        }
        return pages;
    }
    static void WriteRasterPdf(string path,IReadOnlyList<byte[]> images,int width,int height)
    {
        using var output=File.Create(path);var offsets=new List<long>{0};
        void Write(string text)=>output.Write(Encoding.ASCII.GetBytes(text));
        void Object(int id,string body,byte[]? stream=null)
        {
            while(offsets.Count<=id)offsets.Add(0);offsets[id]=output.Position;Write($"{id} 0 obj\n{body}\n");
            if(stream is not null){Write("stream\n");output.Write(stream);Write("\nendstream\n");}Write("endobj\n");
        }
        Write("%PDF-1.4\n");Object(1,"<< /Type /Catalog /Pages 2 0 R >>");
        Object(2,$"<< /Type /Pages /Count {images.Count} /Kids [{string.Join(' ',Enumerable.Range(0,images.Count).Select(i=>$"{3+i*3} 0 R"))}] >>");
        for(int i=0;i<images.Count;i++)
        {
            int page=3+i*3,content=page+1,image=page+2;
            Object(page,$"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 800 500] /Resources << /XObject << /Img {image} 0 R >> >> /Contents {content} 0 R >>");
            byte[] command=Encoding.ASCII.GetBytes("q 800 0 0 500 0 0 cm /Img Do Q");Object(content,$"<< /Length {command.Length} >>",command);
            Object(image,$"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {images[i].Length} >>",images[i]);
        }
        long xref=output.Position;Write($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach(long offset in offsets.Skip(1))Write(offset.ToString("D10",System.Globalization.CultureInfo.InvariantCulture)+" 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }
}
