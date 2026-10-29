using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace EventLens.Core;

public static partial class Exporter
{
    public static async Task<long> WriteAllAsync(string path,IAsyncEnumerable<EventItem> events,bool json,QuerySpec filter,IReadOnlyList<LogSource> sources,Func<SearchProgress> state,CancellationToken token=default)
    {
        if(!System.IO.Path.GetExtension(path).Equals(json?".json":".zip",StringComparison.OrdinalIgnoreCase))throw new ArgumentException(json?"Choose a .json report.":"Choose a .zip CSV bundle; it includes completeness metadata.");
        string temp=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!,".eventlens-"+Guid.NewGuid()+".tmp");long count=0;
        object Metadata()=>new {ExportedUtc=DateTimeOffset.UtcNow,Scope="All matching retrievable records from the captured query; not a transactionally frozen Windows log",Filter=filter,Sources=sources,Records=count,Complete=state().Completed&&state().Issues.Count==0,SourceIssues=state().Issues,Examined=state().Scanned,Warning="Complete means accessible results were exhausted without reported source errors. Retention, disabled auditing, clock skew or concurrent log rotation may omit activity. Correlations do not establish a root cause."};
        try
        {
            await using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
            {
                if(json)
                {
                    using var writer=new Utf8JsonWriter(stream,new(){Indented=true});writer.WriteStartObject();writer.WriteStartArray("Events");
                    await foreach(var item in events.WithCancellation(token)){JsonSerializer.Serialize(writer,item);count++;if(count%128==0)await writer.FlushAsync(token);}
                    writer.WriteEndArray();writer.WritePropertyName("Investigation");JsonSerializer.Serialize(writer,Metadata());writer.WriteEndObject();await writer.FlushAsync(token);
                }
                else
                {
                    using var zip=new ZipArchive(stream,ZipArchiveMode.Create,true);
                    await using(var data=zip.CreateEntry("events.csv").Open())
                    await using(var writer=new StreamWriter(data,new UTF8Encoding(true)))
                    {
                        await writer.WriteLineAsync("TimestampUtc,Severity,Description,User,Computer,Provider,EventId,EventVersion,RecordId,Channel,Origin,Failure,Source,IdentityRelation,OriginalMessage,Xml,ContributingSources,Explanation,CodeDetails,InterpretationRule,Troubleshooting");
                        await foreach(var e in events.WithCancellation(token))
                        {
                            token.ThrowIfCancellationRequested();await writer.WriteLineAsync(string.Join(",",new[]{e.TimestampUtc,e.Severity,e.Title,e.User,e.Computer,e.Provider,e.Id.ToString(),e.Version.ToString(),e.RecordId.ToString(),e.Channel,e.Origin,e.Failure,e.Source,e.Relation,e.OriginalMessage,e.Xml,string.Join("; ",e.ContributingSources),e.Explanation,e.CodeDetails,e.Rule,e.Guidance.Report}.Select(Csv)));count++;
                        }
                    }
                    await using var meta=zip.CreateEntry("investigation.json").Open();await JsonSerializer.SerializeAsync(meta,Metadata(),new JsonSerializerOptions{WriteIndented=true},token);
                }
            }
            token.ThrowIfCancellationRequested();File.Move(temp,path,true);return count;
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
