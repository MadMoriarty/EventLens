using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using EventLens.Core;
using EventLens.Windows;
using Xunit;
using Avalonia.Headless.XUnit;
using EventLens.App.ViewModels;

namespace EventLens.App.Tests;
public class RecoveryAndQueryTests
{
    [AvaloniaFact]public async Task ActualViewModelPagesBackAndExportsBeyondVisibleRows()
    {
        var vm=new MainViewModel((sources,filter)=>new QuerySession([new("fixture","Application")],filter,(s,_)=>new Cursor(s,Enumerable.Range(1,1601).Select(i=>Item(i))))){Range="All available time"};
        string path=Path.Combine(Path.GetTempPath(),"eventlens-vm-"+Guid.NewGuid()+".json");
        try
        {
            await vm.SearchAsync();Assert.Equal(500,vm.Events.Count);var first=vm.Events.Select(e=>e.RecordId).ToArray();await vm.LoadMoreAsync();Assert.True(vm.CanPrevious);Assert.Equal(501,vm.Events[0].RecordId);
            await vm.PreviousPageAsync();Assert.Equal(first,vm.Events.Select(e=>e.RecordId));await vm.CompleteSearchAsync();Assert.Equal(1601,vm.ActiveQuery!.Store.Count);Assert.Equal(500,vm.Events.Count);
            await vm.ExportAllAsync(path,true);using var report=JsonDocument.Parse(await File.ReadAllTextAsync(path));Assert.Equal(1601,report.RootElement.GetProperty("Events").GetArrayLength());Assert.True(report.RootElement.GetProperty("Investigation").GetProperty("Complete").GetBoolean());
        }
        finally{await vm.DisposeAsync();File.Delete(path);}
    }
    [AvaloniaFact]public async Task GuidedAnalysisCountsTheWholeQueryInsteadOfOnlyTheVisiblePage()
    {
        EventItem Crash(int id)=>EventParser.Parse(Item(id).Xml.Replace("Name='Fixture'","Name='Application Error'").Replace("<EventID>1</EventID>","<EventID>1000</EventID>"));
        var vm=new MainViewModel((sources,filter)=>new QuerySession([new("fixture","Application")],filter,(s,_)=>new Cursor(s,Enumerable.Range(1,1501).Select(Crash)))){Range="All available time",Identity="old account filter"};
        try{var plan=InvestigationPlans.All.Single(p=>p.Name=="Application crash");await vm.RunPlanAsync(plan);Assert.Empty(vm.Identity);Assert.Equal(500,vm.Events.Count);var report=await vm.AnalyzeAllAsync(plan);Assert.Contains("1,501 unique matching events",report);Assert.Contains("Application Error / 1000: 1,501",report);Assert.Equal(500,vm.Events.Count);Assert.Equal(1501,vm.ActiveQuery!.Store.Count);}finally{await vm.DisposeAsync();}
    }
    static EventItem Item(int record,string source="fixture")=>EventParser.Parse($"<Event><System><Provider Name='Fixture'/><EventID>1</EventID><EventRecordID>{record}</EventRecordID><TimeCreated SystemTime='2026-10-22T12:00:00Z'/><Channel>Application</Channel><Computer>FixturePC</Computer></System><EventData><Data Name='Text'>record {record}</Data></EventData></Event>",source:source);
    sealed class Cursor(LogSource source,IEnumerable<EventItem> items) : IEventCursor
    {
        readonly IEnumerator<EventItem> values=items.GetEnumerator();public LogSource Source=>source;public EventItem? Head{get;set;}public bool End{get;set;}
        public EventItem? Read(CancellationToken token){token.ThrowIfCancellationRequested();if(values.MoveNext())return values.Current;End=true;return null;}
        public void Dispose()=>values.Dispose();
    }
    sealed class Subscription(Action<EventItem?,string?,Exception?> callback) : ILiveSubscription
    {
        public bool Disposed;public void Start(){}public void Dispose()=>Disposed=true;
        public void Send(int id)=>callback(Item(id),id.ToString(),null);
        public void Fail(int code)=>callback(null,null,new Win32Exception(code));
    }
    static async Task Wait(Func<bool> condition)
    {
        var deadline=DateTime.UtcNow.AddSeconds(5);while(!condition()){if(DateTime.UtcNow>deadline)throw new TimeoutException("Recovery did not reach the expected state");await Task.Delay(20);}
    }
    [Fact]public async Task FullQueryContinuesBeyondTwentyThousandAndExportsEveryMatchOnce()
    {
        using var query=new QuerySession([new("fixture","Application")],new(){From=null},(s,_)=>new Cursor(s,Enumerable.Range(1,21001).Select(i=>Item(i))));
        var first=await query.ReadPageAsync(500,CancellationToken.None);Assert.Equal(500,first.Events.Count);Assert.True(first.HasMore);
        await query.CompleteAsync(null,CancellationToken.None);Assert.True(query.Completed);Assert.Equal(21001,query.Store.Count);Assert.Equal(21001,Assert.Single(query.Store.ReadPage(21000)).RecordId);
        long count=0;await foreach(var e in query.ReadAllAsync())count++;Assert.Equal(21001,count);
    }
    [Fact]public async Task SparseClientFilterContinuesPastEmptyScanBatch()
    {
        using var query=new QuerySession([new("fixture","Application")],new(){From=null,Text="record 25001"},(s,_)=>new Cursor(s,Enumerable.Range(1,25001).Select(i=>Item(i))));
        var first=await query.ReadPageAsync(500,CancellationToken.None);Assert.Empty(first.Events);Assert.True(first.HasMore);await query.CompleteAsync(null,CancellationToken.None);Assert.Equal(25001,Assert.Single(query.Store.ReadPage(0)).RecordId);
    }
    [Fact]public async Task OverlappingSourcesMergeProvenanceBeforeExportEnumeration()
    {
        using var query=new QuerySession([new("a","Application"),new("b","Application")],new(){From=null},(s,_)=>new Cursor(s,[Item(1,s.Computer)]));
        var events=new List<EventItem>();await foreach(var e in query.ReadAllAsync())events.Add(e);var item=Assert.Single(events);Assert.Equal(new[]{"a","b"},item.ContributingSources);
    }
    [Fact]public async Task MissingSourceNeverLooksLikeCompleteCoverage()
    {
        using var query=new QuerySession([new("ok","Application"),new("denied","Security")],new(){From=null},(s,_)=>s.Computer=="denied"?throw new UnauthorizedAccessException():new Cursor(s,[Item(1)]));
        await query.CompleteAsync(null,CancellationToken.None);Assert.True(query.Completed);Assert.Single(query.Issues);Assert.Equal(1,query.Store.Count);
    }
    [Fact]public async Task CancellationRetainsAlreadyConsumedBatchAndCanResume()
    {
        using var cancel=new CancellationTokenSource();
        IEnumerable<EventItem> Values(){for(int i=1;i<=50;i++){if(i==20)cancel.Cancel();yield return Item(i);}}
        using var query=new QuerySession([new("fixture","Application")],new(){From=null},(s,_)=>new Cursor(s,Values()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>query.ReadPageAsync(500,cancel.Token));Assert.Equal(20,query.Store.Count);await query.CompleteAsync(null,CancellationToken.None);Assert.Equal(50,query.Store.Count);
    }
    [Fact]public void NativeSelectionsRequireTheCorrectProvider()
    {
        string native=NativeQuery.Build(new(){From=null,Selectors=InvestigationPlans.All.Single(p=>p.Name=="Application crash").Selectors});Assert.Contains("Provider[@Name='Application Error'] and (EventID=1000)",native);Assert.Contains("Windows Error Reporting",native);
    }
    [Fact]public async Task AutomaticReconnectUsesAcceptedBookmarkAndSuppressesReplayDuplicates()
    {
        var subscriptions=new ConcurrentQueue<Subscription>();var bookmarks=new ConcurrentQueue<string?>();
        await using var monitor=new LiveMonitor(new(){From=null},(s,q,b,callback)=>{bookmarks.Enqueue(b);var sub=new Subscription(callback);subscriptions.Enqueue(sub);return sub;},TimeSpan.FromMilliseconds(20));
        await monitor.StartAsync([new("fixture","Application")],CancellationToken.None);subscriptions.Last().Send(1);subscriptions.Last().Fail(1722);
        await Wait(()=>subscriptions.Count>=2);Assert.Equal("1",bookmarks.Last());Assert.True(subscriptions.First().Disposed);subscriptions.Last().Send(1);subscriptions.Last().Send(2);
        var received=new List<int>();while(monitor.TryRead(out var item))received.Add((int)item.RecordId);Assert.Equal(new[]{1,2},received);Assert.Equal(1,monitor.Duplicates);Assert.True(monitor.Recoveries>=1);
    }
    [Fact]public async Task StaleBookmarkFallsBackWithExplicitGapWarningAndOverlap()
    {
        var subscriptions=new ConcurrentQueue<Subscription>();var requests=new ConcurrentQueue<(string? Bookmark,DateTimeOffset? From)>();
        await using var monitor=new LiveMonitor(new(){From=null},(s,q,b,callback)=>{requests.Enqueue((b,q.From));var sub=new Subscription(callback);subscriptions.Enqueue(sub);return sub;},TimeSpan.FromMilliseconds(20));
        await monitor.StartAsync([new("fixture","Application")],CancellationToken.None);subscriptions.Last().Send(1);subscriptions.Last().Fail(15011);await Wait(()=>subscriptions.Count>=2);
        Assert.Null(requests.Last().Bookmark);Assert.Equal(DateTimeOffset.Parse("2026-10-22T11:58:00Z"),requests.Last().From);Assert.Contains(monitor.TakeIssues(),i=>i.Message.Contains("GAP WARNING"));
    }
    [Fact]public async Task OverflowDoesNotAdvanceRejectedCheckpointAndExplicitRecoveryResumes()
    {
        var subscriptions=new ConcurrentQueue<Subscription>();var checkpoints=new ConcurrentQueue<string?>();
        await using var monitor=new LiveMonitor(new(){From=null},(s,q,b,callback)=>{checkpoints.Enqueue(b);var sub=new Subscription(callback);subscriptions.Enqueue(sub);return sub;},TimeSpan.FromMilliseconds(20));
        await monitor.StartAsync([new("fixture","Application")],CancellationToken.None);var first=subscriptions.Last();for(int i=1;i<=4097;i++)first.Send(i);
        Assert.True(monitor.CollectionStopped);Assert.Equal(1,monitor.Dropped);Assert.Equal("4096",Assert.Single(monitor.CaptureCheckpoints()).Bookmark);
        await Wait(()=>first.Disposed);while(monitor.TryRead(out _)){}monitor.RecoverNow();await Wait(()=>subscriptions.Count>=2);Assert.Equal("4096",checkpoints.Last());subscriptions.Last().Send(4097);Assert.True(monitor.TryRead(out var restored));Assert.Equal(4097,restored.RecordId);
    }
    [Fact]public async Task SessionRestartCanResumeOnlyTheProvidedSourceCheckpoint()
    {
        string? requested=null;var source=new LogSource("fixture","Application");
        await using var monitor=new LiveMonitor(new(){From=null},(s,q,b,callback)=>{requested=b;return new Subscription(callback);},checkpoints:[new(source.Key,"accepted-bookmark",DateTimeOffset.UtcNow)]);
        await monitor.StartAsync([source],CancellationToken.None);Assert.Equal("accepted-bookmark",requested);
    }
    [Fact]public async Task DisconnectBeforeFirstEventBackfillsInsteadOfSilentlyStartingAtNow()
    {
        var subscriptions=new ConcurrentQueue<Subscription>();var starts=new ConcurrentQueue<DateTimeOffset?>();
        await using var monitor=new LiveMonitor(new(){From=null},(s,q,b,callback)=>{starts.Enqueue(q.From);var sub=new Subscription(callback);subscriptions.Enqueue(sub);return sub;},TimeSpan.FromMilliseconds(20));
        await monitor.StartAsync([new("fixture","Application")],CancellationToken.None);subscriptions.First().Fail(1722);await Wait(()=>subscriptions.Count>=2);Assert.NotNull(starts.Last());Assert.Contains(monitor.TakeIssues(),i=>i.Message.Contains("GAP WARNING"));
        subscriptions.First().Send(99);subscriptions.Last().Send(1);Assert.True(monitor.TryRead(out var item));Assert.Equal(1,item.RecordId);Assert.False(monitor.TryRead(out _));
    }
}
