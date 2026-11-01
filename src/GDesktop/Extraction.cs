using System.Diagnostics;
using System.Text;
using System.Text.Json;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace GDesktop;

public static class Extraction
{
    const int MaxCharacters = 2_000_000;
    public static int CloudFlags(FileAttributes attributes) => (int)attributes & (0x1000 | 0x40000 | 0x400000);
    public static Extracted Extract(string path)
    {
        var info=new FileInfo(path);
        // Offline/cloud recall flags: metadata only; never deliberately hydrate a placeholder.
        if (CloudFlags(info.Attributes) != 0) return new([],"online-only");
        if(info.Length>32*1024*1024)return new([],"too-large");
        string ext=info.Extension.ToLowerInvariant();
        if(ext is ".docx" or ".xlsx" or ".pptx")
        {
            if(info.Name.StartsWith("~$",StringComparison.Ordinal))return new([],"metadata");
            return OfficeExtraction.Extract(path,ext,MaxCharacters);
        }
        if(ext is ".doc" or ".xls" or ".ppt")return new([],"legacy-office");
        if(ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff")
            return new([],"ocr-pending",WindowsOcr.ImagePages(path).GetAwaiter().GetResult());
        if(ext==".pdf")
        {
            using var pdf=PdfDocument.Open(path);var pages=new List<ExtractedPage>();var ocrPages=new List<int>();int chars=0;
            foreach(var page in pdf.GetPages())
            {
                string text=ContentOrderTextExtractor.GetText(page);
                if(string.IsNullOrWhiteSpace(text))ocrPages.Add(page.Number);
                int take=Math.Min(text.Length,MaxCharacters-chars);pages.Add(new(page.Number,text[..take]));chars+=take;
                if(chars>=MaxCharacters)return new(pages,"truncated");
            }
            return new(pages,ocrPages.Count>0?"ocr-pending":"indexed",ocrPages);
        }
        string[] supported=[".txt",".md",".csv",".tsv",".log",".json",".xml",".html",".htm",".cs",".sql",".ini",".yaml",".yml"];
        if(!supported.Contains(ext))return new([],"metadata");
        // StreamReader recognizes Unicode BOMs; strict UTF-8 falls back to Windows-1252 for old text files.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string textContent;
        try {textContent=Read(path,new UTF8Encoding(false,true));}
        catch(DecoderFallbackException){textContent=Read(path,Encoding.GetEncoding(1252));}
        return new([new(1,textContent)],textContent.Length>=MaxCharacters?"truncated":"indexed");
    }
    static string Read(string path,Encoding encoding)
    {
        using var reader=new StreamReader(path,encoding,true);char[] buffer=new char[MaxCharacters];int total=0,n;
        while(total<buffer.Length && (n=reader.Read(buffer,total,buffer.Length-total))>0)total+=n;
        return new string(buffer,0,total).Replace('\0',' ');
    }
    public static async Task<Extracted> InProcess(Document d,CancellationToken stop,int? ocrPage=null)
    {
        string exe=Environment.ProcessPath!;
        if(!System.IO.Path.GetFileNameWithoutExtension(exe).Equals("dotnet",StringComparison.OrdinalIgnoreCase))
        {
            exe=System.IO.Path.Combine(AppContext.BaseDirectory,"GDesktop.Extractor.exe");
            if(!File.Exists(exe))throw new IOException("Executável auxiliar de extração ausente. Recompile o aplicativo.");
        }
        var start=new ProcessStartInfo(exe){RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,UseShellExecute=false};
        if(System.IO.Path.GetFileNameWithoutExtension(exe).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(typeof(Extraction).Assembly.Location);
        start.ArgumentList.Add(ocrPage.HasValue?"--ocr":"--extract");start.ArgumentList.Add(d.Path);
        if(ocrPage.HasValue)start.ArgumentList.Add(ocrPage.Value.ToString());
        using var process=Process.Start(start)??throw new IOException("Não foi possível iniciar o extrator.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop);timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var stdout=process.StandardOutput.ReadToEndAsync(timeout.Token);var stderr=process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            while(!process.HasExited)
            {
                if(process.WorkingSet64>512L*1024*1024)throw new IOException("O extrator excedeu o limite de 512 MB.");
                await Task.Delay(150,timeout.Token);
            }
            string output=await stdout;string error=await stderr;
            if(process.ExitCode!=0)throw new IOException(error.Length>500?error[..500]:error);
            return JsonSerializer.Deserialize<Extracted>(output)??throw new IOException("Resposta inválida do extrator.");
        }
        finally {if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);}}
    }
}
