using System.Diagnostics.Eventing.Reader;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Runtime.CompilerServices;
using EventLens.Core;

namespace EventLens.Windows;

public static class NativeQuery
{
    public static string Build(QuerySpec spec)
    {
        if(spec.EventIds.Length>20||spec.EventIds.Any(id=>id<0||id>65535))throw new ArgumentException("Use at most 20 valid event IDs per native query.");
        var clauses=new List<string>();
        if(spec.Selectors.Length>0)
        {
            if(spec.Selectors.Sum(s=>s.Ids.Length)>20)throw new ArgumentException("Use at most 20 provider-scoped event selections.");
            clauses.Add("("+string.Join(" or ",spec.Selectors.Select(s=>
            {
                if(s.Ids.Length==0||s.Ids.Any(id=>id<0||id>65535)||Literal(s.Provider) is not {} provider)throw new ArgumentException("Invalid provider-scoped selection.");
                return "(Provider[@Name="+provider+"] and ("+string.Join(" or ",s.Ids.Select(id=>"EventID="+id))+"))";
            }))+ ")");
        }
        if(spec.From is {} start)clauses.Add("TimeCreated[@SystemTime >= '"+start.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ",System.Globalization.CultureInfo.InvariantCulture)+"']");
        if(spec.To is {} end)clauses.Add("TimeCreated[@SystemTime <= '"+end.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ",System.Globalization.CultureInfo.InvariantCulture)+"']");
        if(spec.EventIds.Length>0)clauses.Add("("+string.Join(" or ",spec.EventIds.Take(20).Select(id=>$"EventID={id}"))+")");
        if(spec.MaximumLevel is {} level)clauses.Add($"(Level >= 1 and Level <= {Math.Clamp(level,0,5)})");
        if(spec.Provider.Length>0 && Literal(spec.Provider) is {} literal)clauses.Add($"Provider[@Name={literal}]");
        return clauses.Count==0?"*":"*[System["+string.Join(" and ",clauses)+"]]";
    }
    static string? Literal(string value)=>!value.Contains('\'')?"'"+value+"'":!value.Contains('"')?"\""+value+"\"":null;
    public static string Friendly(Exception e)
    {
        var root=e.GetBaseException();
        int code=root is System.ComponentModel.Win32Exception win32?win32.NativeErrorCode:root.HResult&0xFFFF;
        if(root is UnauthorizedAccessException || code==5)return "Access denied. This channel may require an Event Log Readers membership, Security-log rights or an elevated launch. EventLens does not bypass permissions.";
        if(code is 1722 or 1727 or 1753)return "Remote Event Log RPC is unavailable. Check reachability, Remote Event Log Management firewall rules, RPC services and the current Windows account's access.";
        if(code==15011)return "Subscription/query became stale (the log may have been cleared or overwritten). Reload history to assess gaps.";
        return root.Message;
    }
}

public interface IEventCursor : IDisposable
{
    LogSource Source {get;}
    EventItem? Head {get;set;}
    bool End {get;set;}
    EventItem? Read(CancellationToken token);
}
sealed class SourceCursor : IEventCursor
{
    public LogSource Source { get; }
    readonly EventLogSession? session;
    readonly EventLogReader reader;
    public EventItem? Head {get;set;}
    public bool End {get;set;}
    public SourceCursor(LogSource source,QuerySpec spec)
    {
        Source=source;
        if(!source.IsFile&&!Local(source.Computer))session=new EventLogSession(source.Computer);
        try
        {
            var query=new EventLogQuery(source.Path,source.IsFile?PathType.FilePath:PathType.LogName,NativeQuery.Build(spec)){ReverseDirection=true,TolerateQueryErrors=false};
            if(session!=null)query.Session=session;
            reader=new EventLogReader(query){BatchSize=16};
        }
        catch {session?.Dispose();throw;}
    }
    public static bool Local(string value)=>value is "" or "."||value.Equals("localhost",StringComparison.OrdinalIgnoreCase)||value.Equals(Environment.MachineName,StringComparison.OrdinalIgnoreCase);
    public EventItem? Read(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();using var registration=token.Register(()=>{try{reader.CancelReading();}catch(ObjectDisposedException){}catch(EventLogException){}});
        using var record=reader.ReadEvent(TimeSpan.FromSeconds(2));
        if(record==null){End=true;return null;}
        string xml=record.ToXml(),message="";
        try{message=record.FormatDescription() ?? "";}catch(EventLogException){}catch(System.ComponentModel.Win32Exception){}
        return EventParser.Parse(xml,message,Source.Key);
    }
    public void Dispose(){reader.Dispose();session?.Dispose();}
}

public sealed class QuerySession : IDisposable
{
    static readonly SemaphoreSlim NativeWorkers=new(4);
    readonly List<IEventCursor> cursors=[];
    readonly List<SourceIssue> issues=[];
    readonly HashSet<string> initializedSources=new(StringComparer.OrdinalIgnoreCase);
    readonly QuerySpec spec;
    readonly IReadOnlyList<LogSource> sources;
    readonly SemaphoreSlim gate=new(1);
    bool initialized,disposed;
    long scanned;
    readonly Func<LogSource,QuerySpec,IEventCursor> cursorFactory;
    public ResultStore Store {get;}
    public QuerySpec Filter=>spec;
    public IReadOnlyList<LogSource> Sources=>sources;
    public long Scanned=>scanned;
    public bool Completed {get;private set;}
    public IReadOnlyList<SourceIssue> Issues=>issues.ToArray();
    public QuerySession(IReadOnlyList<LogSource> sources,QuerySpec spec,Func<LogSource,QuerySpec,IEventCursor>? factory=null,string? cacheDirectory=null)
    {
        if(sources.Count==0||sources.Count>32)throw new ArgumentException("Choose 1-32 event sources.");
        this.sources=sources;this.spec=spec with {To=spec.To??DateTimeOffset.UtcNow};cursorFactory=factory??((source,filter)=>new SourceCursor(source,filter));Store=new(cacheDirectory);
    }
    public async Task<EventPage> ReadPageAsync(int size,CancellationToken token)
    {
        if(size<1||size>1000)throw new ArgumentOutOfRangeException(nameof(size));
        await gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            return await Task.Run(async()=>
            {
                await NativeWorkers.WaitAsync(token);
                try
                {
                    if(!initialized)
                    {
                        foreach(var source in sources.Take(32))
                        {
                            if(initializedSources.Contains(source.Key))continue;
                            token.ThrowIfCancellationRequested();
                            try{cursors.Add(cursorFactory(source,spec));}catch(Exception e)when(e is EventLogException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or IOException){AddIssue(source,NativeQuery.Friendly(e));}
                            initializedSources.Add(source.Key);
                        }
                        initialized=true;
                    }
                    var output=new List<EventItem>();long start=scanned,outputBytes=0;
                    // Client filtering remains bounded per page, even with no matches.
                    try { while(output.Count<size&&scanned-start<25_000&&outputBytes<16L*1024*1024)
                    {
                        token.ThrowIfCancellationRequested();
                        foreach(var cursor in cursors.Where(c=>!c.End&&c.Head==null))Advance(cursor,token);
                        var next=cursors.Where(c=>c.Head!=null).MaxBy(c=>c.Head!.Timestamp ?? DateTimeOffset.MinValue);
                        if(next==null)break;
                        var item=next.Head!;next.Head=null;scanned++;
                        if(EventFilter.Matches(item,spec))
                        {
                            if(spec.Identity.Length>0)item.Relation=string.Join(", ",IdentityQuery.Parse(spec.Identity).Matching(item).Select(i=>i.Role switch {"Subject"=>"Actor / action performed by account","Target"=>"Target / activity against or authenticating account","Member"=>"Group member affected",_=>i.Role}));
                            output.Add(item);outputBytes+=item.EstimatedBytes;
                        }
                    }
                    } finally {Store.AddRange(output);}
                    Completed=!cursors.Any(c=>!c.End||c.Head!=null);
                    return new EventPage(output,!Completed,scanned,issues.ToArray());
                }
                finally{NativeWorkers.Release();}
            },token);
        }
        finally{gate.Release();}
    }
    public async Task CompleteAsync(IProgress<SearchProgress>? progress,CancellationToken token)
    {
        while(!Completed){await ReadPageAsync(500,token).ConfigureAwait(false);progress?.Report(new(Scanned,Store.Count,Completed,Issues));}
    }
    public async IAsyncEnumerable<EventItem> ReadAllAsync(IProgress<SearchProgress>? progress=null,[EnumeratorCancellation]CancellationToken token=default)
    {
        // Finish capture before export so overlapping-source provenance is complete even for early records.
        await CompleteAsync(progress,token).ConfigureAwait(false);
        long offset=0;
        while(true)
        {
            token.ThrowIfCancellationRequested();var page=Store.ReadPage(offset,500,token);
            foreach(var item in page){token.ThrowIfCancellationRequested();yield return item;offset++;}
            if(offset<Store.Count)continue;if(Completed)break;
            await ReadPageAsync(500,token).ConfigureAwait(false);progress?.Report(new(Scanned,Store.Count,Completed,Issues));
        }
    }
    void Advance(IEventCursor cursor,CancellationToken token)
    {
        try{cursor.Head=cursor.Read(token);}
        catch(OperationCanceledException){throw;}
        catch(Exception e)when(e is EventLogException or UnauthorizedAccessException or System.Xml.XmlException or FormatException or System.ComponentModel.Win32Exception or IOException)
        {AddIssue(cursor.Source,NativeQuery.Friendly(e));cursor.End=true;}
    }
    void AddIssue(LogSource source,string message){if(issues.Count<100)issues.Add(new(source.Display,message));}
    public void Dispose()
    {
        // Only dispose after a running native read finishes. Callers retire in the background.
        gate.Wait();try{if(disposed)return;disposed=true;foreach(var c in cursors)c.Dispose();cursors.Clear();Store.Dispose();}finally{gate.Release();}
    }
    public static Task<IReadOnlyList<string>> ChannelsAsync(string computer,CancellationToken token)=>Task.Run<IReadOnlyList<string>>(()=>
    {
        token.ThrowIfCancellationRequested();using var session=SourceCursor.Local(computer)?new EventLogSession():new EventLogSession(computer);
        return session.GetLogNames().OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
    },token);
}

