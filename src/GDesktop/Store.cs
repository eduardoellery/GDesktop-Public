using Microsoft.Data.Sqlite;

namespace GDesktop;

public sealed partial class Store
{
    readonly string connectionString;
    public string DirectoryPath { get; }
    public Store(string directory)
    {
        DirectoryPath = directory;
        Directory.CreateDirectory(directory);
        connectionString = new SqliteConnectionStringBuilder { DataSource = System.IO.Path.Combine(directory, "index.db"), DefaultTimeout = 10 }.ToString();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS roots(id INTEGER PRIMARY KEY,path TEXT NOT NULL UNIQUE COLLATE NOCASE);
            CREATE TABLE IF NOT EXISTS scan_starts(root_id INTEGER NOT NULL,path TEXT NOT NULL COLLATE NOCASE,PRIMARY KEY(root_id,path));
            CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);
            INSERT OR IGNORE INTO settings VALUES('paused','false');
            CREATE TABLE IF NOT EXISTS documents(
              id INTEGER PRIMARY KEY,path TEXT NOT NULL UNIQUE COLLATE NOCASE,name TEXT NOT NULL,
              extension TEXT NOT NULL,size INTEGER NOT NULL,mtime INTEGER NOT NULL,
              state TEXT NOT NULL DEFAULT 'pending',error TEXT,root_id INTEGER NOT NULL,seen TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_documents_state ON documents(state);
            CREATE TABLE IF NOT EXISTS chunks(id INTEGER PRIMARY KEY,document_id INTEGER NOT NULL,page INTEGER NOT NULL,text TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_chunks_document ON chunks(document_id);
            CREATE VIRTUAL TABLE IF NOT EXISTS search USING fts5(name,text,document_id UNINDEXED,page UNINDEXED,tokenize='unicode61 remove_diacritics 2');
            UPDATE documents SET state='pending' WHERE state='processing';
            CREATE TABLE IF NOT EXISTS ocr_jobs(document_id INTEGER NOT NULL,page INTEGER NOT NULL,PRIMARY KEY(document_id,page));
            UPDATE documents SET state='pending',error=NULL WHERE state='needs-ocr';
            """;
        cmd.ExecuteNonQuery();
        // Upgrade existing indexes without discarding their catalog or settings.
        using var columns=c.CreateCommand();columns.CommandText="PRAGMA table_info(documents)";
        bool hasCloudFlags=false;
        using(var reader=columns.ExecuteReader())while(reader.Read())if(reader.GetString(1)=="cloud_flags")hasCloudFlags=true;
        if(!hasCloudFlags){cmd.CommandText="ALTER TABLE documents ADD COLUMN cloud_flags INTEGER NOT NULL DEFAULT -1";cmd.ExecuteNonQuery();}
        using var upgrade=c.BeginTransaction();
        cmd.Transaction=upgrade;
        cmd.CommandText="""
            UPDATE documents SET state='pending',error=NULL
            WHERE extension IN ('.docx','.xlsx','.pptx','.doc','.xls','.ppt') AND state IN ('metadata','error')
            AND NOT EXISTS(SELECT 1 FROM settings WHERE key='office-extractor-version' AND value='1');
            INSERT INTO settings(key,value) VALUES('office-extractor-version','1') ON CONFLICT(key) DO UPDATE SET value='1';
            """;
        cmd.ExecuteNonQuery();upgrade.Commit();
    }
    public SqliteConnection Open() { var c = new SqliteConnection(connectionString); c.Open(); return c; }
    public bool Paused
    {
        get { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT value FROM settings WHERE key='paused'"; return (string?)cmd.ExecuteScalar() == "true"; }
        set { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "UPDATE settings SET value=$v WHERE key='paused'"; cmd.Parameters.AddWithValue("$v", value ? "true" : "false"); cmd.ExecuteNonQuery(); }
    }
    public List<Root> Roots()
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT id,path FROM roots ORDER BY path";
        using var r = cmd.ExecuteReader(); var roots = new List<Root>(); while (r.Read()) roots.Add(new(r.GetInt64(0), r.GetString(1))); return roots;
    }
    public RootAddition AddRoot(string path)
    {
        path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        if (!Directory.Exists(path)) throw new ArgumentException("A pasta não existe ou não está acessível.");
        if (Under(path, DirectoryPath) || Under(DirectoryPath, path)) throw new ArgumentException("Selecione uma pasta que não contenha o banco de dados do aplicativo.");
        using var c=Open();using var tx=c.BeginTransaction();
        var roots=new List<Root>();
        using(var query=c.CreateCommand())
        {
            query.Transaction=tx;query.CommandText="SELECT id,path FROM roots";
            using var reader=query.ExecuteReader();while(reader.Read())roots.Add(new(reader.GetInt64(0),reader.GetString(1)));
        }
        var covering=roots.FirstOrDefault(r=>Under(path,r.Path));
        if(covering is not null)return new(covering,false,[]);
        var root=new Root(Random.Shared.NextInt64(1,9007199254740991L),path);
        var descendants=roots.Where(r=>Under(r.Path,path)).ToList();
        using(var insert=c.CreateCommand())
        {
            insert.Transaction=tx;insert.CommandText="INSERT INTO roots(id,path) VALUES($id,$p)";
            insert.Parameters.AddWithValue("$id",root.Id);insert.Parameters.AddWithValue("$p",path);insert.ExecuteNonQuery();
        }
        foreach(var child in descendants)
        {
            using var merge=c.CreateCommand();merge.Transaction=tx;
            // Transfer ownership, not content: IDs, chunks, FTS entries and pending work survive.
            // Explicitly selected subtrees remain scan starts even below normally excluded folders.
            merge.CommandText="""
                UPDATE documents SET root_id=$parent WHERE root_id=$child;
                INSERT OR IGNORE INTO scan_starts(root_id,path) VALUES($parent,$path);
                INSERT OR IGNORE INTO scan_starts(root_id,path) SELECT $parent,path FROM scan_starts WHERE root_id=$child;
                DELETE FROM scan_starts WHERE root_id=$child;
                DELETE FROM roots WHERE id=$child;
                """;
            merge.Parameters.AddWithValue("$parent",root.Id);merge.Parameters.AddWithValue("$child",child.Id);merge.Parameters.AddWithValue("$path",child.Path);merge.ExecuteNonQuery();
        }
        tx.Commit();return new(root,true,descendants);
    }
    public List<string> ScanStarts(Root root)
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT path FROM scan_starts WHERE root_id=$id";cmd.Parameters.AddWithValue("$id",root.Id);
        var starts=new List<string>{root.Path};using var r=cmd.ExecuteReader();while(r.Read())starts.Add(r.GetString(0));return starts;
    }
    public bool HasRoot(long id)
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT EXISTS(SELECT 1 FROM roots WHERE id=$id)";cmd.Parameters.AddWithValue("$id",id);return (long)cmd.ExecuteScalar()! != 0;
    }
    public bool RemoveRoot(long id)
    {
        using var c=Open();using var tx=c.BeginTransaction();using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="""
          DELETE FROM search WHERE document_id IN (SELECT id FROM documents WHERE root_id=$id);
          DELETE FROM chunks WHERE document_id IN (SELECT id FROM documents WHERE root_id=$id);
          DELETE FROM ocr_jobs WHERE document_id IN (SELECT id FROM documents WHERE root_id=$id);
          DELETE FROM documents WHERE root_id=$id;
          DELETE FROM scan_starts WHERE root_id=$id;
          DELETE FROM roots WHERE id=$id;
          SELECT changes();
          """;
        cmd.Parameters.AddWithValue("$id",id);bool removed=(long)cmd.ExecuteScalar()! != 0;tx.Commit();return removed;
    }
    public static bool Under(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(System.IO.Path.TrimEndingDirectorySeparator(root) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public void Observe(FileInfo f, long rootId, string generation)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = """
          INSERT INTO documents(path,name,extension,size,mtime,root_id,seen,cloud_flags)
          SELECT $p,$n,$e,$s,$m,$r,$g,$flags WHERE EXISTS(SELECT 1 FROM roots WHERE id=$r)
          ON CONFLICT(path) DO UPDATE SET name=$n,extension=$e,size=$s,mtime=$m,root_id=$r,seen=$g,cloud_flags=$flags,
          state=CASE WHEN documents.size!=$s OR documents.mtime!=$m OR documents.cloud_flags!=$flags THEN 'pending' ELSE documents.state END,
          error=CASE WHEN documents.size!=$s OR documents.mtime!=$m OR documents.cloud_flags!=$flags THEN NULL ELSE documents.error END;
          """;
        cmd.Parameters.AddWithValue("$p", f.FullName); cmd.Parameters.AddWithValue("$n", f.Name); cmd.Parameters.AddWithValue("$e", f.Extension.ToLowerInvariant());
        cmd.Parameters.AddWithValue("$s", f.Length); cmd.Parameters.AddWithValue("$m", f.LastWriteTimeUtc.Ticks); cmd.Parameters.AddWithValue("$r", rootId); cmd.Parameters.AddWithValue("$g", generation); cmd.Parameters.AddWithValue("$flags",Extraction.CloudFlags(f.Attributes));cmd.ExecuteNonQuery();
    }
    public void RemoveMissing(long rootId, string generation)
    {
        using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
          DELETE FROM search WHERE document_id IN (SELECT id FROM documents WHERE root_id=$r AND seen!=$g);
          DELETE FROM chunks WHERE document_id IN (SELECT id FROM documents WHERE root_id=$r AND seen!=$g);
          DELETE FROM ocr_jobs WHERE document_id IN (SELECT id FROM documents WHERE root_id=$r AND seen!=$g);
          DELETE FROM documents WHERE root_id=$r AND seen!=$g;
          """;
        cmd.Parameters.AddWithValue("$r", rootId); cmd.Parameters.AddWithValue("$g", generation); cmd.ExecuteNonQuery(); tx.Commit();
    }
    public Document? Next()
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT id,path,name,extension,size,mtime,state,error,cloud_flags FROM documents WHERE state='pending' ORDER BY id LIMIT 1";
        using var r = cmd.ExecuteReader(); return r.Read() ? ReadDocument(r) : null;
    }
    static Document ReadDocument(SqliteDataReader r) => new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt64(4),r.GetInt64(5),r.GetString(6),r.IsDBNull(7)?null:r.GetString(7),r.GetInt32(8));
    public Document? Find(long id)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT id,path,name,extension,size,mtime,state,error,cloud_flags FROM documents WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader(); return r.Read() ? ReadDocument(r) : null;
    }
    public void State(long id, string state, string? error = null)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "UPDATE documents SET state=$s,error=$e WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$s", state); cmd.Parameters.AddWithValue("$e", (object?)error ?? DBNull.Value); cmd.ExecuteNonQuery();
    }
    public void Save(Document d, Extracted result)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        // Removal may happen while an extractor is running. Never recreate deleted index rows.
        using(var check=c.CreateCommand())
        {
            check.Transaction=tx;check.CommandText="SELECT EXISTS(SELECT 1 FROM documents WHERE id=$id AND path=$p AND size=$s AND mtime=$m AND cloud_flags=$flags)";
            check.Parameters.AddWithValue("$id",d.Id);check.Parameters.AddWithValue("$p",d.Path);check.Parameters.AddWithValue("$s",d.Size);check.Parameters.AddWithValue("$m",d.Mtime);
            check.Parameters.AddWithValue("$flags",d.CloudFlags);
            if((long)check.ExecuteScalar()! == 0)return;
        }
        void Run(string sql, params (string,object)[] args) { using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText=sql; foreach(var (k,v) in args)cmd.Parameters.AddWithValue(k,v);cmd.ExecuteNonQuery(); }
        Run("DELETE FROM search WHERE document_id=$id; DELETE FROM chunks WHERE document_id=$id; DELETE FROM ocr_jobs WHERE document_id=$id",("$id",d.Id));
        foreach(int page in result.OcrPages??[])Run("INSERT INTO ocr_jobs(document_id,page) VALUES($id,$p)",("$id",d.Id),("$p",page));
        foreach(var page in result.Pages.DefaultIfEmpty(new ExtractedPage(0,"")))
        {
            // Keep page provenance, and bound chunk size for future semantic retrieval.
            for(int start=0; start < Math.Max(1,page.Text.Length); start+=1800)
            {
                string text=page.Text.Substring(start,Math.Min(2000,page.Text.Length-start));
                Run("INSERT INTO chunks(document_id,page,text) VALUES($id,$p,$t)",("$id",d.Id),("$p",page.Page),("$t",text));
                Run("INSERT INTO search(name,text,document_id,page) VALUES($n,$t,$id,$p)",("$n",d.Name),("$t",text),("$id",d.Id),("$p",page.Page));
            }
        }
        Run("UPDATE documents SET state=$s,error=NULL WHERE id=$id",("$s",result.Status),("$id",d.Id)); tx.Commit();
    }
    public object Status()
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT state,count(*) FROM documents GROUP BY state"; using var r=cmd.ExecuteReader();var counts=new Dictionary<string,long>();while(r.Read())counts[r.GetString(0)]=r.GetInt64(1);
        return new { paused=Paused, counts, roots=Roots(), dataDirectory=DirectoryPath };
    }
    public List<SearchHit> Search(string? query, string? extension, long? rootId)
    {
        query=(query??"").Trim(); if(query.Length>300)throw new ArgumentException("Use uma pesquisa de até 300 caracteres.");
        using var c=Open();using var cmd=c.CreateCommand();
        string filters=" AND ($ext='' OR d.extension=$ext) AND ($root=0 OR d.root_id=$root)";
        if(query.Length==0)
            cmd.CommandText="SELECT d.id,d.name,d.path,d.extension,d.size,d.state,d.error,'' FROM documents d WHERE 1=1"+filters+" ORDER BY d.name LIMIT 100";
        else
        {
            var parsed=new SearchQuery(query);
            // Boolean sets operate on whole documents, not individual chunks/pages.
            // This makes AND across pages and NOT anywhere in a file behave correctly.
            string matches=parsed.PositiveQuery is null?"SELECT NULL document_id,'' excerpt,0.0 score WHERE 0":"SELECT document_id,snippet(search,1,char(1),char(2),' … ',32) excerpt,bm25(search,6.0,1.0) score FROM search WHERE search MATCH $q";
            cmd.CommandText="WITH "+string.Join(",",parsed.Ctes)+", matches AS ("+matches+"), "+"""
                best AS (SELECT document_id,excerpt,row_number() OVER(PARTITION BY document_id ORDER BY score) pos,score FROM matches)
                SELECT d.id,d.name,d.path,d.extension,d.size,d.state,d.error,coalesce(b.excerpt,'')
                FROM documents d
                """+" JOIN "+parsed.Result+" eligible ON d.id=eligible.document_id LEFT JOIN best b ON d.id=b.document_id AND b.pos=1 WHERE 1=1"+filters+" ORDER BY CASE WHEN lower(d.name)=lower($literal) THEN 0 ELSE 1 END,coalesce(b.score,0),d.name LIMIT 100";
            foreach(var (name,value) in parsed.Parameters)cmd.Parameters.AddWithValue(name,value);
            if(parsed.PositiveQuery is not null)cmd.Parameters.AddWithValue("$q",parsed.PositiveQuery);
            cmd.Parameters.AddWithValue("$literal",query);
        }
        cmd.Parameters.AddWithValue("$ext",(extension??"").ToLowerInvariant()); cmd.Parameters.AddWithValue("$root",rootId??0);
        using var reader=cmd.ExecuteReader();var list=new List<SearchHit>();while(reader.Read())list.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetInt64(4),reader.GetString(5),reader.IsDBNull(6)?null:reader.GetString(6),reader.GetString(7)));return list;
    }
}
