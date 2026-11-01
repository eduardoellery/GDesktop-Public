namespace GDesktop;

public sealed partial class Store
{
    public (Document Document,int Page)? NextOcr()
    {
        using var c=Open();using var cmd=c.CreateCommand();
        cmd.CommandText="SELECT d.id,d.path,d.name,d.extension,d.size,d.mtime,d.state,d.error,d.cloud_flags,j.page FROM documents d JOIN ocr_jobs j ON j.document_id=d.id WHERE d.state='ocr-pending' ORDER BY d.id,j.page LIMIT 1";
        using var r=cmd.ExecuteReader();return r.Read()?(ReadDocument(r),r.GetInt32(9)):null;
    }
    public void SaveOcr(Document d,int page,Extracted result)
    {
        using var c=Open();using var tx=c.BeginTransaction();
        using var check=c.CreateCommand();check.Transaction=tx;
        check.CommandText="SELECT EXISTS(SELECT 1 FROM documents WHERE id=$id AND size=$s AND mtime=$m AND cloud_flags=$f AND state='ocr-pending')";
        check.Parameters.AddWithValue("$id",d.Id);check.Parameters.AddWithValue("$s",d.Size);check.Parameters.AddWithValue("$m",d.Mtime);check.Parameters.AddWithValue("$f",d.CloudFlags);
        if((long)check.ExecuteScalar()! == 0)return;
        void Run(string sql,params (string,object)[] args){using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;foreach(var(k,v)in args)cmd.Parameters.AddWithValue(k,v);cmd.ExecuteNonQuery();}
        if(result.Status!="indexed-ocr")
        {
            Run("UPDATE documents SET state=$s WHERE id=$id; DELETE FROM ocr_jobs WHERE document_id=$id",("$s",result.Status),("$id",d.Id));tx.Commit();return;
        }
        Run("DELETE FROM search WHERE document_id=$id AND page=$p; DELETE FROM chunks WHERE document_id=$id AND page=$p",("$id",d.Id),("$p",page));
        foreach(var extracted in result.Pages)
        for(int start=0;start<Math.Max(1,extracted.Text.Length);start+=1800)
        {
            string text=extracted.Text.Substring(start,Math.Min(2000,extracted.Text.Length-start));
            Run("INSERT INTO chunks(document_id,page,text) VALUES($id,$p,$t); INSERT INTO search(name,text,document_id,page) VALUES($n,$t,$id,$p)",("$id",d.Id),("$p",page),("$t",text),("$n",d.Name));
        }
        Run("DELETE FROM ocr_jobs WHERE document_id=$id AND page=$p; UPDATE documents SET state=CASE WHEN EXISTS(SELECT 1 FROM ocr_jobs WHERE document_id=$id) THEN 'ocr-pending' ELSE 'indexed-ocr' END,error=NULL WHERE id=$id",("$id",d.Id),("$p",page));
        tx.Commit();
    }
}
