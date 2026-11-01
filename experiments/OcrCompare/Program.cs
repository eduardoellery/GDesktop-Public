using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TesseractOCR;
using TesseractOCR.Enums;
using Language=Windows.Globalization.Language;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace GDesktop.OcrComparison;

record Input(string Set,int Page,string Path);
record Recognition(string Engine,string Model,string Language,string Set,int Page,string Text,long Milliseconds,double? Confidence);
record Configuration(string Engine,string Model,string Language,long InitializationMs,string? NativeVersion=null,string[]? LoadedLanguages=null,string? Segmentation=null);

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            if(args.Length>0&&args[0]=="--probe")
            {
                var metadata=new List<object>();
                foreach(string model in new[]{"fast","best"})foreach(string language in new[]{"por","eng"})
                {
                    using var engine=new Engine(Path.GetFullPath(Path.Combine("tools/tesseract-data",model)),language,EngineMode.LstmOnly);
                    metadata.Add(new{model,language,version=engine.Version,loadedLanguages=engine.LoadedLanguages.Select(l=>l.ToString()).ToArray(),pageSegmentation=engine.DefaultPageSegMode.ToString()});
                }
                Console.WriteLine(JsonSerializer.Serialize(metadata,new JsonSerializerOptions{WriteIndented=true}));return 0;
            }
            string root=Path.GetFullPath(args.Length>0?args[0]:Directory.GetCurrentDirectory());
            string output=Path.Combine(root,"tmp","ocr-compare");Directory.CreateDirectory(output);
            var inputs=new List<Input>{new("portuguese",1,Path.Combine(root,"tmp/windows-ocr-published/comprovante.png"))};
            foreach(string set in new[]{"200","300"})for(int page=1;page<=5;page++)inputs.Add(new("strata-"+set,page,Path.Combine(root,set=="200"?"tmp/windows-ocr-sample":"tmp/windows-ocr-sample-300",$"page-{page}.png")));
            foreach(var input in inputs)if(!File.Exists(input.Path))throw new FileNotFoundException("Imagem de comparação ausente.",input.Path);
            var hashes=new Dictionary<string,string>();foreach(var input in inputs)hashes[input.Path]=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(input.Path)));
            var results=new List<Recognition>();var configs=new List<Configuration>();
            var watch=Stopwatch.StartNew();
            var windows=OcrEngine.TryCreateFromLanguage(new Language("pt-BR"))??throw new InvalidOperationException("Windows OCR pt-BR indisponível.");
            configs.Add(new("Windows","native","pt-BR",watch.ElapsedMilliseconds));
            foreach(var input in inputs)
            {
                watch.Restart();var file=await StorageFile.GetFileFromPathAsync(Path.GetFullPath(input.Path));
                using var stream=await file.OpenAsync(FileAccessMode.Read);var decoder=await BitmapDecoder.CreateAsync(stream);
                using var bitmap=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied);
                var page=await windows.RecognizeAsync(bitmap);
                results.Add(new("Windows","native","pt-BR",input.Set,input.Page,string.Join('\n',page.Lines.Select(l=>l.Text)),watch.ElapsedMilliseconds,null));
            }
            foreach(string model in new[]{"fast","best"})foreach(string language in new[]{"por","eng"})
            {
                watch.Restart();using var engine=new Engine(Path.Combine(root,"tools/tesseract-data",model),language,EngineMode.LstmOnly);
                configs.Add(new("Tesseract",model,language,watch.ElapsedMilliseconds,engine.Version,engine.LoadedLanguages.Select(l=>l.ToString()).ToArray(),engine.DefaultPageSegMode.ToString()));
                foreach(var input in inputs)
                {
                    // English is an extra language-correct setup for the real English PDF.
                    if(language=="eng"&&input.Set=="portuguese")continue;
                    watch.Restart();using var image=TesseractOCR.Pix.Image.LoadFromFile(input.Path);
                    using var page=engine.Process(image);
                    string text=page.Text;double confidence=page.MeanConfidence;
                    results.Add(new("Tesseract",model,language,input.Set,input.Page,text,watch.ElapsedMilliseconds,confidence));
                    await File.WriteAllTextAsync(Path.Combine(output,$"tesseract-{model}-{language}-{input.Set}-{input.Page}.txt"),text);
                    Console.WriteLine($"Tesseract {model}/{language} {input.Set} p{input.Page}: {watch.ElapsedMilliseconds}ms");
                }
            }
            foreach(var input in inputs)
                if(hashes[input.Path]!=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(input.Path))))throw new IOException("Imagem original alterada durante o teste.");
            const string portuguese="COMPROVANTE DE PAGAMENTO Aluguel de outubro Valor: R$ 1.234,56 Data: 31/10/2026 Referência: OCRTESTE9371 Pagamento confirmado.";
            var synthetic=results.Where(r=>r.Set=="portuguese").Select(r=>new{r.Engine,r.Model,r.Language,r.Milliseconds,characterErrorRate=CharacterError(portuguese,r.Text),wordErrorRate=WordError(portuguese,r.Text)}).ToArray();
            var timing=results.GroupBy(r=>(r.Engine,r.Model,r.Language,r.Set)).Select(g=>new{g.Key.Engine,g.Key.Model,g.Key.Language,g.Key.Set,pages=g.Count(),totalMs=g.Sum(r=>r.Milliseconds)}).ToArray();
            var report=new{generated=DateTime.UtcNow,wrapper=typeof(Engine).Assembly.GetName().Version?.ToString(),windowsLanguages=OcrEngine.AvailableRecognizerLanguages.Select(l=>l.LanguageTag).ToArray(),mode="LSTM, default automatic page segmentation, unmodified identical PNGs; OMP_THREAD_LIMIT=1 for Tesseract",configs,synthetic,timing,inputsUnchanged=true,results};
            await File.WriteAllTextAsync(Path.Combine(output,"results.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine(JsonSerializer.Serialize(new{report.wrapper,synthetic,timing,output},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    static string Normalize(string text)=>Regex.Replace(text.Normalize(NormalizationForm.FormKC).ToLowerInvariant(),@"\s+"," ").Trim();
    static double CharacterError(string expected,string actual){expected=Normalize(expected);actual=Normalize(actual);return (double)Distance(expected.ToCharArray(),actual.ToCharArray())/expected.Length;}
    static double WordError(string expected,string actual)
    {
        var a=Regex.Matches(Normalize(expected),@"[\p{L}\p{N}]+").Select(m=>m.Value).ToArray();
        var b=Regex.Matches(Normalize(actual),@"[\p{L}\p{N}]+").Select(m=>m.Value).ToArray();return (double)Distance(a,b)/a.Length;
    }
    static int Distance<T>(IReadOnlyList<T> expected,IReadOnlyList<T> actual)
    {
        var previous=Enumerable.Range(0,actual.Count+1).ToArray();var current=new int[actual.Count+1];
        for(int i=1;i<=expected.Count;i++)
        {
            current[0]=i;
            for(int j=1;j<=actual.Count;j++)current[j]=Math.Min(Math.Min(current[j-1]+1,previous[j]+1),previous[j-1]+(EqualityComparer<T>.Default.Equals(expected[i-1],actual[j-1])?0:1));
            (previous,current)=(current,previous);
        }
        return previous[actual.Count];
    }
}
