using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace EventLens.Core;

/// <summary>Private, temporary investigation spool. Only a page is materialized in memory.</summary>
public sealed class ResultStore : IDisposable
{
    readonly SqliteConnection connection;
    readonly object gate=new();
    public string Path {get;}
    public long Count {get;private set;}
    bool disposed;
    public ResultStore(string? directory=null)
    {
        directory??=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"EventLens-QueryCache");
        Directory.CreateDirectory(directory);Path=System.IO.Path.Combine(directory,Guid.NewGuid().ToString("N")+".sqlite");
        connection=new(new SqliteConnectionStringBuilder{DataSource=Path,Pooling=false}.ToString());connection.Open();
        using var cmd=connection.CreateCommand();cmd.CommandText="PRAGMA cache_size=-4096; PRAGMA journal_mode=DELETE; CREATE TABLE records(seq INTEGER PRIMARY KEY, eventkey TEXT NOT NULL UNIQUE, xml TEXT NOT NULL, message TEXT NOT NULL, source TEXT NOT NULL, relation TEXT NOT NULL, sources TEXT NOT NULL);";cmd.ExecuteNonQuery();
    }
    public void AddRange(IEnumerable<EventItem> items,CancellationToken token=default)
    {
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);using var transaction=connection.BeginTransaction();
            long inserted=0;using var cmd=connection.CreateCommand();cmd.Transaction=transaction;
            cmd.CommandText="INSERT OR IGNORE INTO records(eventkey,xml,message,source,relation,sources) VALUES($key,$xml,$message,$source,$relation,$sources)";
            foreach(var name in new[]{"$key","$xml","$message","$source","$relation","$sources"})cmd.Parameters.Add(name,SqliteType.Text);
            foreach(var item in items)
            {
                token.ThrowIfCancellationRequested();cmd.Parameters[0].Value=item.Key;cmd.Parameters[1].Value=item.Xml;cmd.Parameters[2].Value=item.OriginalMessage;cmd.Parameters[3].Value=item.Source;cmd.Parameters[4].Value=item.Relation;
                cmd.Parameters[5].Value=JsonSerializer.Serialize(item.ContributingSources.Append(item.Source).Distinct());
                if(cmd.ExecuteNonQuery()==0)
                {
                    using var read=connection.CreateCommand();read.Transaction=transaction;read.CommandText="SELECT sources FROM records WHERE eventkey=$key";read.Parameters.AddWithValue("$key",item.Key);
                    var existing=JsonSerializer.Deserialize<string[]>((string)read.ExecuteScalar()!)??[];
                    using var update=connection.CreateCommand();update.Transaction=transaction;update.CommandText="UPDATE records SET sources=$sources WHERE eventkey=$key";update.Parameters.AddWithValue("$key",item.Key);update.Parameters.AddWithValue("$sources",JsonSerializer.Serialize(existing.Concat(item.ContributingSources).Append(item.Source).Distinct()));update.ExecuteNonQuery();
                }
                else inserted++;
            }
            transaction.Commit();Count+=inserted;
        }
    }
    public IReadOnlyList<EventItem> ReadPage(long offset,int size=500,CancellationToken token=default)
    {
        if(offset<0||size<1||size>1000)throw new ArgumentOutOfRangeException(nameof(size));
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);using var cmd=connection.CreateCommand();cmd.CommandText="SELECT xml,message,source,relation,sources FROM records WHERE seq>$offset ORDER BY seq LIMIT $size";cmd.Parameters.AddWithValue("$size",size);cmd.Parameters.AddWithValue("$offset",offset);
            using var reader=cmd.ExecuteReader();var result=new List<EventItem>();long bytes=0;while(reader.Read())
            {
                token.ThrowIfCancellationRequested();var item=EventParser.Parse(reader.GetString(0),reader.GetString(1),reader.GetString(2));
                if(result.Count>0&&bytes+item.EstimatedBytes>32L*1024*1024)break;
                item.Relation=reader.GetString(3);item.ContributingSources.AddRange(JsonSerializer.Deserialize<string[]>(reader.GetString(4))??[]);result.Add(item);bytes+=item.EstimatedBytes;
            }
            return result;
        }
    }
    public void Dispose()
    {
        lock(gate){if(disposed)return;disposed=true;connection.Dispose();foreach(var suffix in new[]{"","-journal","-wal","-shm"})try{File.Delete(Path+suffix);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}
