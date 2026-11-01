using System.Collections.Concurrent;

namespace GDesktop;

public sealed class Indexer(Store store, ILogger<Indexer> logger):BackgroundService
{
    readonly ConcurrentDictionary<long,FileSystemWatcher> watchers=new();
    readonly ConcurrentQueue<string> messages=new();
    readonly object watcherGate=new();
    int requested=1;
    public volatile bool Scanning;
    public string? Current {get;private set;}
    public string[] Messages=>messages.ToArray();
    public bool IsBusy=>Scanning || Current is not null || Volatile.Read(ref requested)!=0;
    public void RequestScan()=>Interlocked.Exchange(ref requested,1);
    public RootAddition AddRoot(string path)
    {
        lock(watcherGate)
        {
            var result=store.AddRoot(path);
            foreach(var child in result.Consolidated)if(watchers.TryRemove(child.Id,out var watcher))watcher.Dispose();
            if(result.Added){Watch(result.Root);RequestScan();}
            return result;
        }
    }
    public bool RemoveRoot(long id)
    {
        lock(watcherGate)
        {
            bool removed=store.RemoveRoot(id);
            if(watchers.TryRemove(id,out var watcher))watcher.Dispose();
            return removed;
        }
    }
    void Message(string value){messages.Enqueue(value);while(messages.Count>12)messages.TryDequeue(out _);logger.LogWarning("{Message}",value);}
    protected override Task ExecuteAsync(CancellationToken stoppingToken)=>Task.Run(()=>Run(stoppingToken),stoppingToken);
    async Task Run(CancellationToken stop)
    {
        DateTime nextScan=DateTime.MinValue;
        try
        {
            while(!stop.IsCancellationRequested)
            {
                if(store.Paused){await Task.Delay(300,stop);continue;}
                if(Interlocked.Exchange(ref requested,0)==1 || DateTime.UtcNow>=nextScan)
                {
                    Scanning=true;
                    try{foreach(var root in store.Roots()){Watch(root);await Scan(root,stop);}}
                    finally{Scanning=false;Current=null;}
                    nextScan=DateTime.UtcNow.AddMinutes(5);
                }
                var d=store.Next();
                if(d is null && store.NextOcr() is {} job)
                {
                    d=job.Document;Current=$"{d.Path} — OCR, página {job.Page}";
                    try
                    {
                        var result=await Extraction.InProcess(d,stop,job.Page);
                        var fresh=new FileInfo(d.Path);
                        if(!fresh.Exists || fresh.Length!=d.Size || fresh.LastWriteTimeUtc.Ticks!=d.Mtime || Extraction.CloudFlags(fresh.Attributes)!=d.CloudFlags){store.State(d.Id,"pending");RequestScan();}
                        else store.SaveOcr(d,job.Page,result);
                    }
                    catch(OperationCanceledException) when(stop.IsCancellationRequested){throw;}
                    catch(Exception ex){store.State(d.Id,"error",ex.Message);Message($"Falha no OCR de {d.Name}: {ex.Message}");}
                    finally{Current=null;}
                    continue;
                }
                if(d is null){await Task.Delay(500,stop);continue;}
                Current=d.Path;store.State(d.Id,"processing");
                try
                {
                    var result=await Extraction.InProcess(d,stop);
                    var fresh=new FileInfo(d.Path);
                    if(!fresh.Exists || fresh.Length!=d.Size || fresh.LastWriteTimeUtc.Ticks!=d.Mtime || Extraction.CloudFlags(fresh.Attributes)!=d.CloudFlags){store.State(d.Id,"pending");RequestScan();}
                    else store.Save(d,result);
                }
                catch(OperationCanceledException) when(stop.IsCancellationRequested){store.State(d.Id,"pending");throw;}
                catch(Exception ex){store.State(d.Id,"error",ex.Message);Message($"Falha em {d.Name}: {ex.Message}");}
                finally{Current=null;}
            }
        }
        catch(OperationCanceledException) when(stop.IsCancellationRequested){}
        finally{foreach(var watcher in watchers.Values)watcher.Dispose();}
    }
    void Watch(Root root)
    {
        lock(watcherGate)
        {
        if(!store.HasRoot(root.Id))return;
        if(watchers.ContainsKey(root.Id)||!Directory.Exists(root.Path))return;
        try
        {
            var w=new FileSystemWatcher(root.Path){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.LastWrite|NotifyFilters.Size|NotifyFilters.Attributes};
            w.Changed+=(_,_)=>RequestScan();w.Created+=(_,_)=>RequestScan();w.Deleted+=(_,_)=>RequestScan();w.Renamed+=(_,_)=>RequestScan();
            w.Error+=(_,_)=>{Message($"Monitoramento interrompido em {root.Path}; será feita reconciliação.");if(watchers.TryRemove(root.Id,out var old))old.Dispose();RequestScan();};
            watchers[root.Id]=w;w.EnableRaisingEvents=true;
        }catch(Exception ex){Message($"Sem monitoramento em {root.Path}: {ex.Message}");}
        }
    }
    async Task Scan(Root root,CancellationToken stop)
    {
        string generation=Guid.NewGuid().ToString("N");bool complete=true;
        var directories=new Stack<string>(store.ScanStarts(root));
        var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excluded=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"node_modules",".git",".vs",".dotnet",".nuget","bin","obj","$RECYCLE.BIN","System Volume Information"};
        while(directories.Count>0)
        {
            stop.ThrowIfCancellationRequested();
            while(store.Paused){if(!store.HasRoot(root.Id))return;await Task.Delay(300,stop);}
            if(!store.HasRoot(root.Id))return;
            var dir=directories.Pop();Current=dir;
            if(!visited.Add(dir))continue;
            try
            {
                foreach(var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    stop.ThrowIfCancellationRequested();
                    if((entry.Attributes&FileAttributes.Directory)!=0)
                    {
                        if(!excluded.Contains(entry.Name)&&(entry.Attributes&FileAttributes.ReparsePoint)==0&&!Store.Under(entry.FullName,store.DirectoryPath))directories.Push(entry.FullName);
                    }
                    else if(entry is FileInfo file)
                    {
                        try{store.Observe(file,root.Id,generation);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){complete=false;Message($"Não foi possível catalogar {file.FullName}: {ex.Message}");}
                    }
                }
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){complete=false;Message($"Pasta inacessível {dir}: {ex.Message}");}
        }
        // Never interpret a disconnected drive or inaccessible folder as mass deletion.
        if(complete)store.RemoveMissing(root.Id,generation);
    }
}
