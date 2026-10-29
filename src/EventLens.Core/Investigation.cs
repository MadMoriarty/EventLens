using System.Text.Json;
using System.Text;
using System.Globalization;

namespace EventLens.Core;

public sealed record IdentityQuery(string Name,string Domain,string Sid)
{
    public static IdentityQuery Parse(string text)
    {
        text=text.Trim();if(text.StartsWith("S-1-",StringComparison.OrdinalIgnoreCase))return new("","",text);
        if(text.Contains('\\')){var p=text.Split('\\',2);return new(p[1],p[0],"");}
        if(text.Contains('@')){var p=text.Split('@',2);return new(p[0],p[1],"");}
        return new(text,"","");
    }
    public IEnumerable<IdentityRef> Matching(EventItem item)=>item.Identities.Where(i=>Sid.Length>0?i.Sid.Equals(Sid,StringComparison.OrdinalIgnoreCase):i.Name.Equals(Name,StringComparison.OrdinalIgnoreCase)&&(Domain.Length==0||i.Domain.Equals(Domain,StringComparison.OrdinalIgnoreCase)));
    public bool Matches(EventItem item)=>Matching(item).Any();
    public string AmbiguityNotice(IEnumerable<EventItem> items)
    {
        if(Sid.Length>0||Domain.Length>0)return "Matching recorded identity fields only; domain aliases and name-to-SID resolution are not inferred.";
        var scopes=items.SelectMany(e=>Matching(e).Select(i=>i.Domain.Length>0?i.Domain:e.Computer)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return scopes.Length>1?"Ambiguous unqualified username across "+string.Join(", ",scopes)+". These accounts are displayed together but not treated as one identity. Search DOMAIN\\user or SID to narrow it.":"Unqualified username: missing domain/SID data may be ambiguous. Specify DOMAIN\\user or SID for stronger identification.";
    }
}

public static class EventFilter
{
    public static bool Matches(EventItem item,QuerySpec spec)
    {
        if(spec.From!=null&&(item.Timestamp==null||item.Timestamp<spec.From))return false;
        if(spec.To!=null&&(item.Timestamp==null||item.Timestamp>spec.To))return false;
        if(spec.EventIds.Length>0&&!spec.EventIds.Contains(item.Id))return false;
        if(spec.Selectors.Length>0&&!spec.Selectors.Any(s=>s.Provider.Equals(item.Provider,StringComparison.OrdinalIgnoreCase)&&s.Ids.Contains(item.Id)))return false;
        if(spec.Provider.Length>0&&!item.Provider.Equals(spec.Provider,StringComparison.OrdinalIgnoreCase))return false;
        if(spec.MaximumLevel!=null&&(item.Level==0||item.Level>spec.MaximumLevel))return false;
        if(spec.Identity.Length>0&&!IdentityQuery.Parse(spec.Identity).Matches(item))return false;
        if(spec.Category!=null&&item.Category!=spec.Category)return false;
        if(!Contains(item.Computer,spec.Computer)||!Contains(item.Origin,spec.Origin)||!Contains(item.Protocol,spec.Protocol)||!Contains(item.Failure,spec.Failure))return false;
        return spec.Text.Length==0||new[]{item.Title,item.OriginalMessage,item.Provider,item.Computer,item.Id.ToString(),item.Xml}.Any(v=>Contains(v,spec.Text));
    }
    static bool Contains(string value,string needle)=>needle.Length==0||value.Contains(needle,StringComparison.OrdinalIgnoreCase);
}

public sealed record EventGroup(string Description,string Identity,string Source,string Failure,int Count,DateTimeOffset? First,DateTimeOffset? Last,IReadOnlyList<EventItem> Items);
public sealed record CorrelationFinding(string Title,string Evidence,string Confidence,IReadOnlyList<EventItem> Events);
public static class Investigator
{
    public static IReadOnlyList<EventGroup> Group(IEnumerable<EventItem> events)=>events.DistinctBy(e=>e.Key).GroupBy(e=>new {e.Provider,e.Id,e.Category,e.Failure,Identity=PrincipalKey(e),e.Origin,e.Protocol,e.Computer})
        .Select(g=>new EventGroup(g.First().Title,g.First().User,g.Key.Origin,g.Key.Failure,g.Count(),g.Min(e=>e.Timestamp),g.Max(e=>e.Timestamp),g.OrderBy(e=>e.Timestamp).ToArray())).OrderByDescending(g=>g.Count).ToArray();
    static string PrincipalKey(EventItem e)=>e.Identities.FirstOrDefault(i=>i.Role is "Target" or "Account")?.StableKey(e.Computer)??e.Key;
    public static IReadOnlyList<CorrelationFinding> Correlate(IEnumerable<EventItem> events)
    {
        var result=new List<CorrelationFinding>();
        foreach(var account in events.DistinctBy(e=>e.Key).Where(e=>e.Known&&e.Timestamp!=null).GroupBy(PrincipalKey))
        {
            var ordered=account.OrderBy(e=>e.Timestamp).ToArray();
            foreach(var locked in ordered.Where(e=>e.Category==EventCategory.Lockout))
            {
                var before=ordered.Where(e=>e.Category==EventCategory.FailedAuthentication&&e.Timestamp<=locked.Timestamp&&e.Timestamp>=locked.Timestamp!.Value.AddMinutes(-15)).ToArray();
                var after=ordered.Where(e=>e.Timestamp>locked.Timestamp&&e.Timestamp<=locked.Timestamp!.Value.AddMinutes(30)&&(e.Id==4767||e.Category==EventCategory.SuccessfulAuthentication)).ToArray();
                string origins=string.Join(", ",before.Select(e=>e.Origin).Where(v=>v.Length>0).Distinct(StringComparer.OrdinalIgnoreCase));
                result.Add(new("Lockout sequence: "+locked.User,$"{before.Length} recorded failures in the preceding 15 minutes. Recorded source clues: {(origins.Length>0?origins:"none")}. Caller: {locked.Origin}. {after.Count(e=>e.Id==4767)} unlock records and {after.Count(e=>e.Category==EventCategory.SuccessfulAuthentication)} successful authentications in the following 30 minutes. Temporal association does not establish causation; other DCs and the originating workstation may contain missing evidence.","Temporal correlation; not a root-cause conclusion",before.Concat([locked]).Concat(after).ToArray()));
            }
            foreach(var repeated in account.Where(e=>e.Category==EventCategory.FailedAuthentication).GroupBy(e=>new {e.Origin,e.Failure,Bucket=e.Timestamp!.Value.ToUnixTimeSeconds()/900}))
                if(repeated.Count()>=3)result.Add(new("Repeated authentication failures: "+repeated.First().User,$"{repeated.Count()} failures within the same UTC 15-minute interval. Recorded source: {repeated.Key.Origin}. Reason: {repeated.Key.Failure}. This is a pattern, not attribution of malicious activity.","Recorded pattern",repeated.ToArray()));
        }
        return result;
    }
}

public static partial class Exporter
{
    public static async Task WriteAsync(string path,IEnumerable<EventItem> events,bool json,CancellationToken cancellation=default)
    {
        if(Path.GetExtension(path).Equals(".evtx",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Investigation reports cannot overwrite EVTX log files. Choose JSON or CSV.");
        string directory=Path.GetDirectoryName(Path.GetFullPath(path))!;string temp=Path.Combine(directory,".eventlens-"+Guid.NewGuid()+".tmp");
        try
        {
            await using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
            {
                if(json)await JsonSerializer.SerializeAsync(stream,new { ExportedUtc=DateTimeOffset.UtcNow,Scope="Loaded results only; may be a partial investigation",Events=events },new JsonSerializerOptions { WriteIndented=true },cancellation);
                else
                {
                    await using var writer=new StreamWriter(stream,new UTF8Encoding(true),leaveOpen:true);
                    await writer.WriteLineAsync("TimestampUtc,Severity,Description,User,Computer,Provider,EventId,RecordId,Channel,Origin,Failure,Source,IdentityRelation,OriginalMessage,Xml,ContributingSources,Explanation,CodeDetails,InterpretationRule,Troubleshooting");
                    foreach(var e in events)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        await writer.WriteLineAsync(string.Join(",",new[]{e.TimestampUtc,e.Severity,e.Title,e.User,e.Computer,e.Provider,e.Id.ToString(),e.RecordId.ToString(),e.Channel,e.Origin,e.Failure,e.Source,e.Relation,e.OriginalMessage,e.Xml,string.Join("; ",e.ContributingSources),e.Explanation,e.CodeDetails,e.Rule,e.Guidance.Report}.Select(Csv)));
                    }
                    await writer.FlushAsync(cancellation);
                }
                await stream.FlushAsync(cancellation);
            }
            File.Move(temp,path,true);
        }
        finally { if(File.Exists(temp))File.Delete(temp); }
    }
    static string Csv(string text)
    {
        // Excel may execute formula-like cells even when CSV-quoted.
        if(text.TrimStart().StartsWith('=')||text.TrimStart().StartsWith('+')||text.TrimStart().StartsWith('-')||text.TrimStart().StartsWith('@')||text.StartsWith('\t')||text.StartsWith('\r'))text="'"+text;
        return "\""+text.Replace("\"","\"\"")+"\"";
    }
}
