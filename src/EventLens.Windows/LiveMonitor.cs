using System.Collections.Concurrent;
using System.Diagnostics.Eventing.Reader;
using System.Threading.Channels;
using EventLens.Core;

namespace EventLens.Windows;

public sealed record LiveCheckpoint(string SourceKey,string Bookmark,DateTimeOffset? Timestamp);
public interface ILiveSubscription : IDisposable {void Start();}
public delegate ILiveSubscription LiveFactory(LogSource source,QuerySpec filter,string? bookmark,Action<EventItem?,string?,Exception?> deliver);

sealed class NativeLiveSubscription : ILiveSubscription
{
    readonly EventLogSession? session;
    readonly EventLogWatcher watcher;
    public NativeLiveSubscription(LogSource source,QuerySpec filter,string? bookmark,Action<EventItem?,string?,Exception?> deliver)
    {
        if(!SourceCursor.Local(source.Computer))session=new(source.Computer);
        try
        {
            var query=new EventLogQuery(source.Path,PathType.LogName,NativeQuery.Build(filter));if(session!=null)query.Session=session;
            watcher=new(query,bookmark==null?null:new EventBookmark(bookmark),bookmark!=null||filter.From!=null);
            watcher.EventRecordWritten+=(_,args)=>
            {
                using var record=args.EventRecord;
                if(args.EventException is {} error){deliver(null,null,error);return;}if(record==null)return;
                try
                {
                    string message="";try{message=record.FormatDescription()??"";}catch(EventLogException){}catch(System.ComponentModel.Win32Exception){}
                    deliver(EventParser.Parse(record.ToXml(),message,source.Key),record.Bookmark.BookmarkXml,null);
                }
                catch(Exception e)when(e is EventLogException or FormatException or System.Xml.XmlException or System.ComponentModel.Win32Exception){deliver(null,null,e);}
            };
        }
        catch{session?.Dispose();throw;}
    }
    public void Start()=>watcher.Enabled=true;
    public void Dispose(){try{watcher.Enabled=false;}finally{watcher.Dispose();session?.Dispose();}}
}

/// <summary>Bounded delivery, checkpoint-after-retention, and supervised reconnect. No silent gap-free guarantee.</summary>
public sealed class LiveMonitor : IAsyncDisposable
{
    sealed class State(LogSource source)
    {
        public LogSource Source=source;public ILiveSubscription? Subscription;public string? Bookmark;public DateTimeOffset? Timestamp;
        public bool Failed,Stale;public int Attempts,Epoch;public DateTimeOffset RetryAt;
    }
    static readonly SemaphoreSlim StartupWorkers=new(2);
    readonly object gate=new();
    readonly List<State> states=[];
    readonly Channel<EventItem> events=Channel.CreateBounded<EventItem>(new BoundedChannelOptions(4096){SingleReader=true,SingleWriter=false,FullMode=BoundedChannelFullMode.Wait});
    readonly ConcurrentQueue<SourceIssue> issues=new();
    readonly QuerySpec spec;
    readonly LiveFactory factory;
    readonly TimeSpan retryDelay;
    readonly CancellationTokenSource shutdown=new();
    readonly HashSet<string> seen=new(StringComparer.Ordinal);
    readonly Queue<string> recent=new();
    readonly IReadOnlyList<LiveCheckpoint> initial;
    Task? startTask,recoveryTask,detachTask;
    long received,dropped,queuedBytes,recoveries,duplicates;
    int stopping,disposed;
    public DateTimeOffset StartedUtc {get;}=DateTimeOffset.UtcNow;
    public long Received=>Interlocked.Read(ref received);
    public long Dropped=>Interlocked.Read(ref dropped);
    public long Recoveries=>Interlocked.Read(ref recoveries);
    public long Duplicates=>Interlocked.Read(ref duplicates);
    public bool CollectionStopped=>Volatile.Read(ref stopping)!=0;
    public int FaultedSources{get{lock(gate)return states.Count(s=>s.Failed);}}
    public int SourceCount{get{lock(gate)return states.Count;}}
    public int ActiveSourceCount{get{lock(gate)return states.Count(s=>!s.Failed&&s.Subscription!=null);}}
    public LiveMonitor(QuerySpec spec,LiveFactory? factory=null,TimeSpan? retryDelay=null,IReadOnlyList<LiveCheckpoint>? checkpoints=null)
    {
        this.spec=spec with{From=null,To=null};this.factory=factory??((s,q,b,callback)=>new NativeLiveSubscription(s,q,b,callback));this.retryDelay=retryDelay??TimeSpan.FromSeconds(2);initial=checkpoints??[];
    }
    void Issue(State state,string message){if(issues.Count<100)issues.Enqueue(new(state.Source.Display,message));}
    public async Task StartAsync(IEnumerable<LogSource> sources,CancellationToken token)
    {
        lock(gate)
        {
            if(startTask!=null)throw new InvalidOperationException("This live monitor has already started.");
            foreach(var source in sources.Where(s=>!s.IsFile).DistinctBy(s=>s.Key).Take(16))
            {
                var point=initial.FirstOrDefault(c=>c.SourceKey==source.Key);states.Add(new(source){Bookmark=point?.Bookmark,Timestamp=point?.Timestamp});
            }
            startTask=Task.Run(async()=>
            {
                await StartupWorkers.WaitAsync(token);
                try{foreach(var state in states){token.ThrowIfCancellationRequested();await ConnectAsync(state,token);}}
                finally{StartupWorkers.Release();}
            },token);
        }
        await startTask;recoveryTask=RecoverLoopAsync();
    }
    async Task ConnectAsync(State state,CancellationToken token)
    {
        await Task.Run(()=>
        {
            ILiveSubscription? old;string? bookmark;bool stale;int attempts,epoch;DateTimeOffset? timestamp;
            lock(gate){old=state.Subscription;state.Subscription=null;bookmark=state.Bookmark;attempts=state.Attempts;stale=state.Stale||(attempts>0&&bookmark==null);timestamp=state.Timestamp;epoch=++state.Epoch;}
            try{old?.Dispose();}catch(Exception e){Issue(state,"Previous subscription cleanup: "+e.Message);}
            token.ThrowIfCancellationRequested();if(Volatile.Read(ref disposed)!=0||CollectionStopped)return;
            if(stale)
            {
                bookmark=null;Issue(state,"GAP WARNING: bookmark is stale or unavailable. Replaying retained history with a two-minute overlap. Cleared/overwritten events and clock-skewed records may be unrecoverable.");
            }
            var filter=stale?spec with{From=(timestamp??StartedUtc).AddMinutes(-2)}:spec;
            ILiveSubscription? created=null;
            try
            {
                created=factory(state.Source,filter,bookmark,(item,point,error)=>OnDelivery(state,epoch,item,point,error));
                lock(gate){state.Subscription=created;state.Failed=false;state.Stale=false;}
                created.Start();
                if(Volatile.Read(ref disposed)!=0||CollectionStopped){created.Dispose();return;}
                lock(gate){if(!state.Failed)state.Attempts=0;}
                if(attempts>0||bookmark!=null){Interlocked.Increment(ref recoveries);Issue(state,"Subscription reattached; retained events after the accepted checkpoint are replayed. Review gap warnings and source coverage.");}
            }
            catch(Exception e)when(e is EventLogException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or IOException or FormatException)
            {
                try{created?.Dispose();}catch(Exception){}lock(gate){state.Subscription=null;}Fault(state,e);
            }
        },token);
    }
    void Fault(State state,Exception error)
    {
        lock(gate)
        {
            state.Failed=true;state.Attempts++;int code=error is System.ComponentModel.Win32Exception w?w.NativeErrorCode:error.HResult&0xffff;
            if(code==15011||error is EventLogReadingException){state.Stale=true;}
            state.RetryAt=DateTimeOffset.UtcNow+TimeSpan.FromMilliseconds(Math.Min(30000,retryDelay.TotalMilliseconds*Math.Pow(2,Math.Min(state.Attempts-1,4))));
        }
        Issue(state,"Live source interrupted; recovery scheduled. "+NativeQuery.Friendly(error));
    }
    void OnDelivery(State state,int epoch,EventItem? item,string? point,Exception? error)
    {
        lock(gate)
        {
            if(state.Epoch!=epoch)return;
            if(error!=null){if(Volatile.Read(ref disposed)==0)Fault(state,error);return;}
        }
        if(item==null||CollectionStopped||Volatile.Read(ref disposed)!=0)return;
        Interlocked.Increment(ref received);
        lock(gate)
        {
            if(state.Epoch!=epoch||CollectionStopped||Volatile.Read(ref disposed)!=0)return;
            if(!EventFilter.Matches(item,spec)){state.Bookmark=point??state.Bookmark;state.Timestamp=item.Timestamp??state.Timestamp;return;}
            if(seen.Contains(item.Key)){Interlocked.Increment(ref duplicates);state.Bookmark=point??state.Bookmark;state.Timestamp=item.Timestamp??state.Timestamp;return;}
            if(spec.Identity.Length>0)item.Relation=string.Join(", ",IdentityQuery.Parse(spec.Identity).Matching(item).Select(i=>i.Role));
            long bytes=Interlocked.Add(ref queuedBytes,item.EstimatedBytes);
            if(bytes>64L*1024*1024||!events.Writer.TryWrite(item))
            {
                Interlocked.Add(ref queuedBytes,-item.EstimatedBytes);Interlocked.Increment(ref dropped);
                if(Interlocked.Exchange(ref stopping,1)==0)
                {
                    foreach(var source in states){source.Failed=true;source.Attempts++;source.RetryAt=DateTimeOffset.UtcNow;}
                    Issue(state,"GAP WARNING: live buffer overflow; collection stopped. The rejected event did NOT advance the checkpoint. Resume display, then Recover live to replay available history.");
                    detachTask=Task.Run(DetachAll);
                }
                return;
            }
            seen.Add(item.Key);recent.Enqueue(item.Key);while(recent.Count>8192)seen.Remove(recent.Dequeue());
            state.Bookmark=point??state.Bookmark;state.Timestamp=item.Timestamp??state.Timestamp;
        }
    }
    async Task RecoverLoopAsync()
    {
        try
        {
            while(!shutdown.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(100,Math.Min(1000,retryDelay.TotalMilliseconds))),shutdown.Token);
                if(CollectionStopped)continue;if(detachTask!=null)await detachTask;State[] targets;lock(gate)targets=states.Where(s=>s.Failed&&s.RetryAt<=DateTimeOffset.UtcNow).ToArray();
                foreach(var state in targets)await ConnectAsync(state,shutdown.Token);
            }
        }
        catch(OperationCanceledException)when(shutdown.IsCancellationRequested){}
    }
    public void RecoverNow()
    {
        if(Volatile.Read(ref disposed)!=0)return;Interlocked.Exchange(ref stopping,0);lock(gate)foreach(var state in states.Where(s=>s.Failed))state.RetryAt=DateTimeOffset.UtcNow;
    }
    public IReadOnlyList<LiveCheckpoint> CaptureCheckpoints(){lock(gate)return states.Where(s=>s.Bookmark!=null).Select(s=>new LiveCheckpoint(s.Source.Key,s.Bookmark!,s.Timestamp)).ToArray();}
    public bool TryRead(out EventItem item)
    {
        if(events.Reader.TryRead(out var value)){Interlocked.Add(ref queuedBytes,-value.EstimatedBytes);item=value;return true;}item=null!;return false;
    }
    public IReadOnlyList<SourceIssue> TakeIssues(){var result=new List<SourceIssue>();while(issues.TryDequeue(out var issue))result.Add(issue);return result;}
    void DetachAll()
    {
        ILiveSubscription[] subscriptions;lock(gate){subscriptions=states.Where(s=>s.Subscription!=null).Select(s=>s.Subscription!).ToArray();foreach(var state in states)state.Subscription=null;}
        foreach(var subscription in subscriptions)try{subscription.Dispose();}catch(EventLogException){}catch(ObjectDisposedException){}
    }
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;Interlocked.Exchange(ref stopping,1);shutdown.Cancel();
        if(startTask!=null)try{await startTask;}catch(OperationCanceledException){}
        if(recoveryTask!=null)try{await recoveryTask;}catch(OperationCanceledException){}
        if(detachTask!=null)await detachTask;await Task.Run(DetachAll);events.Writer.TryComplete();shutdown.Dispose();
    }
}
