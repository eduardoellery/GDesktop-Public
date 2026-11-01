using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace GDesktop;

public static class WindowsOcr
{
    public static async Task<List<int>> ImagePages(string path)
    {
        var info=new FileInfo(path);
        if(Extraction.CloudFlags(info.Attributes)!=0)return [];
        var file=await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        using var stream=await file.OpenAsync(FileAccessMode.Read);var decoder=await BitmapDecoder.CreateAsync(stream);
        return Enumerable.Range(1,checked((int)decoder.FrameCount)).ToList();
    }
    public static async Task<Extracted> Extract(string path,int pageNumber)
    {
        path=Path.GetFullPath(path);var info=new FileInfo(path);
        // Recheck immediately before any content access, including PDF rendering.
        if(Extraction.CloudFlags(info.Attributes)!=0)return new([],"online-only");
        if(info.Length>32*1024*1024)return new([],"too-large");
        var engine=OcrEngine.TryCreateFromLanguage(new Language("pt-BR"))??OcrEngine.TryCreateFromUserProfileLanguages();
        if(engine is null)throw new IOException("Instale um idioma de reconhecimento de texto nas configurações do Windows para usar OCR.");
        var file=await StorageFile.GetFileFromPathAsync(path);
        string text;
        if(info.Extension.Equals(".pdf",StringComparison.OrdinalIgnoreCase))
        {
            var pdf=await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
            using var page=pdf.GetPage(checked((uint)(pageNumber-1)));
            double scale=Math.Min(200.0/96,2800/Math.Max(page.Size.Width,page.Size.Height));
            using var rendered=new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(rendered,new Windows.Data.Pdf.PdfPageRenderOptions{
                DestinationWidth=(uint)Math.Max(1,Math.Round(page.Size.Width*scale)),
                DestinationHeight=(uint)Math.Max(1,Math.Round(page.Size.Height*scale)),
                BackgroundColor=new Windows.UI.Color{A=255,R=255,G=255,B=255}});
            rendered.Seek(0);text=await Recognize(rendered,engine);
        }
        else
        {
            using var stream=await file.OpenAsync(FileAccessMode.Read);
            text=await Recognize(stream,engine,pageNumber-1);
        }
        return new([new(pageNumber,text)],"indexed-ocr");
    }
    static async Task<string> Recognize(IRandomAccessStream stream,OcrEngine engine,int frameNumber=0)
    {
        var decoder=await BitmapDecoder.CreateAsync(stream);
        var frame=await decoder.GetFrameAsync(checked((uint)frameNumber));
        double scale=Math.Min(1,2800.0/Math.Max(frame.PixelWidth,frame.PixelHeight));
        var transform=new BitmapTransform{ScaledWidth=(uint)Math.Max(1,frame.PixelWidth*scale),ScaledHeight=(uint)Math.Max(1,frame.PixelHeight*scale)};
        using var bitmap=await frame.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,transform,ExifOrientationMode.RespectExifOrientation,ColorManagementMode.DoNotColorManage);
        var result=await engine.RecognizeAsync(bitmap).AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        string text=string.Join('\n',result.Lines.Select(l=>l.Text)).Replace('\0',' ');
        return text.Length>2_000_000?text[..2_000_000]:text;
    }
}
