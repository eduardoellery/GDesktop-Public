using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace GDesktop;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(args.Length==3&&args[0]=="--ocr")
        {
            try{Console.OutputEncoding=System.Text.Encoding.UTF8;Console.Write(JsonSerializer.Serialize(WindowsOcr.Extract(args[1],int.Parse(args[2])).GetAwaiter().GetResult()));return 0;}
            catch(Exception ex){Console.Error.Write(ex.Message);return 1;}
        }
        if(args.Length==2&&args[0]=="--extract")
        {
            try{Console.OutputEncoding=System.Text.Encoding.UTF8;Console.Write(JsonSerializer.Serialize(Extraction.Extract(args[1])));return 0;}
            catch(Exception ex){Console.Error.Write(ex.Message);return 1;}
        }
        string? Option(string name){int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
        string data=Option("--data")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GDesktop");
        using var mutex=new Mutex(true,"Local\\GDesktop-"+Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(data).ToLowerInvariant())))[..20],out bool created);
        if(!created){Console.Error.WriteLine("O GDesktop já está em execução para este índice.");return 2;}
        string contentRoot=Directory.Exists(Path.Combine(AppContext.BaseDirectory,"wwwroot"))?AppContext.BaseDirectory:Directory.GetCurrentDirectory();
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{Args=[],ContentRootPath=contentRoot});
        // Windows EventLog may require elevated registration. A user-session app must not depend on it.
        builder.Logging.ClearProviders();builder.Logging.AddSimpleConsole();builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(o=>o.Listen(IPAddress.Loopback,int.TryParse(Option("--port"),out int port)?port:0));
        var store=new Store(data);builder.Services.AddSingleton(store);builder.Services.AddSingleton<Indexer>();builder.Services.AddHostedService(s=>s.GetRequiredService<Indexer>());
        var app=builder.Build();var indexer=app.Services.GetRequiredService<Indexer>();
        string session=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string code=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));DateTime codeExpiry=DateTime.UtcNow.AddMinutes(2);object codeLock=new();
        string baseUrl="";
        app.Use(async(ctx,next)=>
        {
            if(ctx.Request.Host.Value!=new Uri(baseUrl).Authority){ctx.Response.StatusCode=400;return;}
            ctx.Response.Headers.ContentSecurityPolicy="default-src 'self'; script-src 'self'; style-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
            ctx.Response.Headers.XContentTypeOptions="nosniff";ctx.Response.Headers.CacheControl="no-store";ctx.Response.Headers["Referrer-Policy"]="no-referrer";
            if(ctx.Request.Headers.TryGetValue("Origin",out var origin)&&origin!=baseUrl){ctx.Response.StatusCode=403;return;}
            if(ctx.Request.Path.StartsWithSegments("/api")&&ctx.Request.Path!="/api/session")
            {
                if(ctx.Request.Cookies["gd-session"]!=session){ctx.Response.StatusCode=401;return;}
                if(ctx.Request.Method!="GET"&&ctx.Request.Headers["X-GDesktop"]!="1"){ctx.Response.StatusCode=403;return;}
            }
            try{await next(ctx);}catch(ArgumentException ex){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(new{error=ex.Message});}
            catch(Exception ex){app.Logger.LogError(ex,"Request failed");ctx.Response.StatusCode=500;await ctx.Response.WriteAsJsonAsync(new{error="Não foi possível concluir a operação. Consulte o log do aplicativo."});}
        });
        app.UseDefaultFiles();app.UseStaticFiles();
        app.MapPost("/api/session",(Login login,HttpContext ctx)=>
        {
            lock(codeLock)
            {
                if(DateTime.UtcNow>codeExpiry||string.IsNullOrEmpty(code)||login.Code!=code)return Results.Unauthorized();
                code="";
                ctx.Response.Cookies.Append("gd-session",session,new CookieOptions{HttpOnly=true,SameSite=SameSiteMode.Strict,IsEssential=true,Path="/"});
                return Results.Ok();
            }
        });
        app.MapGet("/api/status",()=>new{version=typeof(Program).Assembly.GetName().Version?.ToString(3),catalog=store.Status(),scanning=indexer.Scanning,current=indexer.Current,messages=indexer.Messages});
        app.MapGet("/api/search",(string? q,string? ext,long? root)=>store.Search(q,ext,root));
        app.MapGet("/api/readme",()=>new{text=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"README.md"))});
        app.MapPost("/api/roots",(AddRoot root)=>indexer.AddRoot(root.Path));
        app.MapPost("/api/roots/{id:long}/remove",(long id)=>indexer.RemoveRoot(id)?Results.Ok():Results.NotFound());
        app.MapPost("/api/pick-folder",async()=>
        {
            var completion=new TaskCompletionSource<string?>();
            var thread=new Thread(()=>{try{using var picker=new FolderBrowserDialog{Description="Escolha uma pasta para indexar",UseDescriptionForTitle=true};completion.SetResult(picker.ShowDialog()==DialogResult.OK?picker.SelectedPath:null);}catch(Exception ex){completion.SetException(ex);}});
            thread.SetApartmentState(ApartmentState.STA);thread.Start();return new{path=await completion.Task};
        });
        app.MapPost("/api/pause",()=>{store.Paused=true;return Results.Ok();});
        app.MapPost("/api/resume",()=>{store.Paused=false;return Results.Ok();});
        app.MapPost("/api/rescan",()=>{indexer.RequestScan();return Results.Ok();});
        app.MapPost("/api/documents/{id:long}/reveal",(long id)=>
        {
            var d=store.Find(id);if(d is null)return Results.NotFound();if(!File.Exists(d.Path))return Results.BadRequest(new{error="O arquivo não está disponível."});
            var start=new ProcessStartInfo("explorer.exe"){UseShellExecute=true};start.Arguments="/select,\""+d.Path+"\"";Process.Start(start);return Results.Ok();
        });
        app.MapPost("/api/documents/{id:long}/open",(long id)=>
        {
            var d=store.Find(id);if(d is null)return Results.NotFound();
            string[] allowed=[".txt",".md",".pdf",".csv",".png",".jpg",".jpeg",".bmp",".tif",".tiff",".docx",".xlsx",".pptx"];
            if(!allowed.Contains(d.Extension))return Results.BadRequest(new{error="Use Mostrar na pasta para este tipo de arquivo."});
            if(!File.Exists(d.Path))return Results.BadRequest(new{error="O arquivo não está disponível."});
            Process.Start(new ProcessStartInfo(d.Path){UseShellExecute=true});return Results.Ok();
        });
        app.MapPost("/api/retry",()=>{using var c=store.Open();using var cmd=c.CreateCommand();cmd.CommandText="UPDATE documents SET state='pending',error=NULL WHERE state='error'";cmd.ExecuteNonQuery();return Results.Ok();});
        app.StartAsync().GetAwaiter().GetResult();
        baseUrl=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        string BrowserUrl(){lock(codeLock){code=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));codeExpiry=DateTime.UtcNow.AddMinutes(2);return baseUrl+"/#code="+code;}}
        Console.WriteLine("GDesktop: "+baseUrl);
        if(args.Contains("--headless"))
        {
            // Explicit test automation mode: write the one-time bootstrap URL only to its requested file.
            var ready=Option("--ready-file");if(ready is not null)File.WriteAllText(ready,BrowserUrl());
            app.WaitForShutdownAsync().GetAwaiter().GetResult();return 0;
        }
        ApplicationConfiguration.Initialize();
        void OpenBrowser()=>Process.Start(new ProcessStartInfo(BrowserUrl()){UseShellExecute=true});
        using var menu=new ContextMenuStrip();menu.Items.Add("Abrir pesquisa",null,(_,_)=>OpenBrowser());
        menu.Items.Add("Pausar / retomar",null,(_,_)=>store.Paused=!store.Paused);
        menu.Items.Add("Sair",null,(_,_)=>Application.ExitThread());
        using var idleIcon=TrayIcons.Create(System.Drawing.Color.FromArgb(32,162,102));
        using var busyIcon=TrayIcons.Create(System.Drawing.Color.FromArgb(239,169,41));
        using var pausedIcon=TrayIcons.Create(System.Drawing.Color.FromArgb(110,121,139),true);
        using var tray=new NotifyIcon{Icon=idleIcon,Text="GDesktop — busca local",ContextMenuStrip=menu,Visible=true};tray.DoubleClick+=(_,_)=>OpenBrowser();
        using var trayTimer=new System.Windows.Forms.Timer{Interval=750};
        string lastTrayState="";
        trayTimer.Tick+=(_,_)=>
        {
            string state=store.Paused?"paused":indexer.IsBusy?"busy":"idle";
            if(state==lastTrayState)return;lastTrayState=state;
            tray.Icon=state=="paused"?pausedIcon:state=="busy"?busyIcon:idleIcon;
            tray.Text=state=="paused"?"GDesktop — indexação pausada":state=="busy"?"GDesktop — indexando arquivos":"GDesktop — índice atualizado";
        };
        trayTimer.Start();
        OpenBrowser();Application.Run();tray.Visible=false;app.StopAsync().GetAwaiter().GetResult();app.DisposeAsync().AsTask().GetAwaiter().GetResult();return 0;
    }
}
