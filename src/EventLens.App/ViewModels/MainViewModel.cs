using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Threading;
using EventLens.Core;
using EventLens.Windows;
namespace EventLens.App.ViewModels;
public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
    protected bool Set<T>(ref T field,T value,[CallerMemberName]string? name=null){if(EqualityComparer<T>.Default.Equals(field,value))return false;field=value;Changed(name);return true;}
}
public sealed class ActionCommand(Func<Task> action,Action<Exception> onError) : ICommand
{
    public event EventHandler? CanExecuteChanged {add{}remove{}}
    public bool CanExecute(object? parameter)=>true;
    public async void Execute(object? parameter){try{await action();}catch(Exception e){onError(e);}}
}
public sealed class MainViewModel : Observable,IAsyncDisposable
{
    public const int ResultLimit=20_000;
    public const long PayloadBudget=128L*1024*1024;
    public ObservableCollection<EventItem> Events {get;}=[];
    public ObservableCollection<SavedSearch> Saved {get;}=[];
    public ObservableCollection<string> SourceNotices {get;}=[];
    public string[] Navigation {get;}=["Overview","All Events","Security","System","Application","Custom Event Channels","User Investigation","Live Monitoring","Saved Searches","Imported EVTX Files"];
    public string[] Ranges {get;}=["Last 15 minutes","Last hour","Last 24 hours","Last 7 days","Custom UTC range","All available time"];
    public string[] Categories {get;}=["All categories",..Enum.GetNames<EventCategory>()];
    public string[] Severities {get;}=["All severities","Critical + Error","Warning and higher","Information and higher"];
    public List<string> Computers {get;set;}=["localhost"];
    public List<string> Channels {get;set;}=["Security","System","Application"];
    public List<LogSource> Imported {get;}=[];
    public AppSettings Settings {get;private set;}
    public PortableSettings SettingsStore {get;}=new();
    public ICommand SearchCommand {get;}
    public ICommand MoreCommand {get;}
    public ICommand CancelCommand {get;}
    public ICommand WatchCommand {get;}
    public ICommand PauseCommand {get;}
    public Action? ScrollNewest;
    public Action? NotifyFailure;
    CancellationTokenSource? queryCancellation;
    QuerySession? query;
    LiveMonitor? live;
    readonly HashSet<string> keys=new(StringComparer.Ordinal);
    readonly DispatcherTimer timer;
    readonly Func<IReadOnlyList<LogSource>,QuerySpec,QuerySession> queryFactory;
    int generation;
    bool closing,hasMore,isBusy,paused,autoScroll=true,sound,liveDisplay;
    long evicted;
    long retainedBytes;
    long pageOffset;
    InvestigationPlan? activePlan;
    EventSelector[] selectors=[];
    IReadOnlyList<LiveCheckpoint> lastLivePoints=[];
    string lastLiveFilter="";
    public string PlanStatus=>selectors.Length==0?"":$"Provider-scoped selection: {activePlan?.Name??"saved investigation"}. Guided investigations > Clear selection to remove it.";
    public bool HasPlan=>selectors.Length>0;
    public bool HasQuery=>query!=null;
    public QuerySession? ActiveQuery=>query;
    public InvestigationPlan? ActivePlan=>activePlan;
    public bool CanPrevious=>pageOffset>0&&!IsBusy&&!IsWatching&&!liveDisplay;
    public string PageStatus=>liveDisplay?$"Live view: {Events.Count:N0} displayed; historical exports use the last captured search. Run Search again to include a later interval.":query==null?"":$"Rows {((Events.Count==0)?0:pageOffset+1):N0}–{pageOffset+Events.Count:N0} · {query.Store.Count:N0} matches cached · {(query.Completed?(query.Issues.Count==0?"accessible query exhausted":"query exhausted with source errors"):"more history remains")}";
    public IReadOnlyList<LogSource> SelectedSources()=>Sources();
    public QuerySpec CurrentFilter()=>Spec();
    string section="All Events",range="Last 24 hours",category="All categories",severity="All severities",identity="",text="",ids="",provider="",origin="",computer="",protocol="",failure="",status="Ready",liveStatus="Not watching",notice="",customFrom="",customTo="",searchName="";
    EventItem? selected;
    SavedSearch? selectedSaved;
    public MainViewModel(Func<IReadOnlyList<LogSource>,QuerySpec,QuerySession>? queryFactory=null)
    {
        this.queryFactory=queryFactory??((sources,spec)=>new QuerySession(sources,spec));
        Settings=SettingsStore.Load();Computers=Settings.Computers is {Count:>0}?Settings.Computers:["localhost"];
        foreach(var saved in Settings.Searches)Saved.Add(saved);
        if(Saved.Count==0)
        {
            Saved.Add(new("Failed authentication",new(){Category=EventCategory.FailedAuthentication}));
            Saved.Add(new("Account lockouts",new(){Category=EventCategory.Lockout}));
            Saved.Add(new("Service failures",new(){Category=EventCategory.Service,MaximumLevel=3}));
            Saved.Add(new("Unexpected shutdowns",new(){Category=EventCategory.Shutdown}));
        }
        Notice="Searches read event logs without modifying them. "+SettingsStore.Warning;
        SearchCommand=Command(()=>SearchAsync());MoreCommand=Command(LoadMoreAsync);CancelCommand=Command(()=>{Cancel();return Task.CompletedTask;});
        WatchCommand=Command(ToggleWatchAsync);PauseCommand=Command(()=>{Paused=!Paused;LiveStatus=Paused?"Display paused; bounded collection continues":"Live display resumed";return Task.CompletedTask;});
        timer=new DispatcherTimer(TimeSpan.FromMilliseconds(120),DispatcherPriority.Background,(_,_)=>DrainLive());timer.Start();
    }
    public string Section {get=>section;set{if(Set(ref section,value)){Changed(nameof(IsSaved));Changed(nameof(IsInvestigation));Changed(nameof(IsCustomTime));}}}
    ICommand Command(Func<Task> action)=>new ActionCommand(action,e=>Status=NativeQuery.Friendly(e));
    public bool IsSaved=>Section=="Saved Searches";
    public bool IsInvestigation=>Section is "User Investigation" or "Live Monitoring";
    public string Range {get=>range;set{if(Set(ref range,value))Changed(nameof(IsCustomTime));}}
    public bool IsCustomTime=>Range=="Custom UTC range";
    public string Category {get=>category;set=>Set(ref category,value);}
    public string Severity {get=>severity;set=>Set(ref severity,value);}
    public string Identity {get=>identity;set=>Set(ref identity,value);}
    public string Text {get=>text;set=>Set(ref text,value);}
    public string Ids {get=>ids;set=>Set(ref ids,value);}
    public string Provider {get=>provider;set=>Set(ref provider,value);}
    public string Origin {get=>origin;set=>Set(ref origin,value);}
    public string Computer {get=>computer;set=>Set(ref computer,value);}
    public string Protocol {get=>protocol;set=>Set(ref protocol,value);}
    public string Failure {get=>failure;set=>Set(ref failure,value);}
    public string CustomFrom {get=>customFrom;set=>Set(ref customFrom,value);}
    public string CustomTo {get=>customTo;set=>Set(ref customTo,value);}
    public string SearchName {get=>searchName;set=>Set(ref searchName,value);}
    public string Status {get=>status;set=>Set(ref status,value);}
    public string Notice {get=>notice;set=>Set(ref notice,value);}
    public string LiveStatus {get=>liveStatus;set=>Set(ref liveStatus,value);}
    public bool IsBusy {get=>isBusy;set{Set(ref isBusy,value);Changed(nameof(IsIdle));Changed(nameof(CanPrevious));}}
    public bool IsIdle=>!IsBusy;
    public bool HasMore {get=>hasMore;set=>Set(ref hasMore,value);}
    public bool Paused {get=>paused;set{Set(ref paused,value);Changed(nameof(PauseLabel));}}
    public string PauseLabel=>Paused?"Resume display":"Pause display";
    public bool AutoScroll {get=>autoScroll;set=>Set(ref autoScroll,value);}
    public bool Sound {get=>sound;set=>Set(ref sound,value);}
    public bool IsWatching=>live!=null;
    public string WatchLabel=>IsWatching?"Stop watching":"Watch User / Live";
    public string SourcesLabel=>$"{Computers.Count} computer(s) · {string.Join(", ",Channels)} · {Imported.Count} EVTX file(s)";
    public string Scope=>"Summary covers the displayed page. Scan all matches searches beyond it; exports include captured query coverage. Retention and audit policy may omit activity.";
    public string Stats=>$"{Events.Count:N0} loaded   ·   {Events.Count(e=>e.Category==EventCategory.SuccessfulAuthentication):N0} successful   ·   {Events.Count(e=>e.Category==EventCategory.FailedAuthentication):N0} failures   ·   {Events.Count(e=>e.Category==EventCategory.Lockout):N0} lockouts   ·   {Events.Count(e=>e.Category==EventCategory.PasswordChange):N0} password attempts   ·   {Events.Count(e=>e.Category==EventCategory.AccountChange):N0} account changes";
    public string IdentityNotice=>Identity.Length>0?IdentityQuery.Parse(Identity).AmbiguityNotice(Events):"Timeline timestamps use local time; raw details and exports preserve UTC. Different machine clocks may be skewed.";
    public string Origins=>"Recorded sources: "+string.Join(", ",Events.Select(e=>e.Origin).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).Take(15));
    public EventItem? Selected {get=>selected;set{if(Set(ref selected,value)){Changed(nameof(DetailTitle));Changed(nameof(Explanation));Changed(nameof(Metadata));Changed(nameof(RawProperties));Changed(nameof(RawXml));Changed(nameof(Original));Changed(nameof(CodeDetails));Changed(nameof(GuidanceText));Changed(nameof(ReferenceLinks));}}}
    public string DetailTitle=>Selected?.Title ?? "Select an event";
    public string Explanation=>Selected?.Explanation ?? "Read the explanation alongside the original provider message, payload and XML.";
    public string Original=>Selected?.OriginalMessage ?? "";
    public string CodeDetails=>Selected?.CodeDetails ?? "Select an event to inspect its recorded codes.";
    public string GuidanceText=>Selected?.Guidance.Text ?? "Select an event for investigation steps and relevant references.";
    public IReadOnlyList<KnowledgeLink> ReferenceLinks=>Selected?.Guidance.References ?? Troubleshooting.Sources;
    public string RawXml=>Selected?.Xml ?? "";
    public string Metadata=>Selected is {} e?$"UTC  {e.TimestampUtc}\nComputer  {e.Computer}\nChannel  {e.Channel}\nProvider  {e.Provider}\nEvent / version / record  {e.Id} / {e.Version} / {e.RecordId}\nSchema  {SchemaSupport.Notice(e)}\nActivity ID  {e.ActivityId}\nProcess / thread  {e.ProcessId} / {e.ThreadId}\nUser  {e.User}\nOrigin  {e.Origin}\nProtocol  {e.Protocol}\nFailure  {e.Failure}\nIdentity relationship  {e.Relation}\n\nRule  {e.Rule}\n\n"+string.Join("\n",e.Identities.Select(i=>$"{i.Role}: {i.Display} [{i.Sid}]")):"";
    public string RawProperties=>Selected is {} e?string.Join("\n\n",e.Data.Select(p=>p.Key+"\n"+p.Value)):"";
    public SavedSearch? SelectedSaved {get=>selectedSaved;set=>Set(ref selectedSaved,value);}
    public void SourcesChanged(){Changed(nameof(SourcesLabel));}
    QuerySpec Spec()
    {
        DateTimeOffset? from=Range switch {"Last 15 minutes"=>DateTimeOffset.UtcNow.AddMinutes(-15),"Last hour"=>DateTimeOffset.UtcNow.AddHours(-1),"Last 7 days"=>DateTimeOffset.UtcNow.AddDays(-7),"All available time"=>null,_=>DateTimeOffset.UtcNow.AddHours(-24)};
        DateTimeOffset? to=null;
        if(IsCustomTime)
        {
            if(!DateTimeOffset.TryParse(CustomFrom,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal,out var a)||!DateTimeOffset.TryParse(CustomTo,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal,out var b)||b<a)throw new FormatException("Enter valid start/end timestamps (for example 2026-10-22T08:00:00Z), with end after start.");from=a;to=b;
        }
        var eventIds=new List<int>();foreach(var token in Ids.Split([',',';',' '],StringSplitOptions.RemoveEmptyEntries)){if(!int.TryParse(token,out var id)||id<0||id>65535)throw new FormatException("Event IDs must be integers between 0 and 65535.");eventIds.Add(id);}
        if(eventIds.Count>20)throw new FormatException("Use at most 20 native Event ID predicates per search.");
        return new(){Selectors=selectors,From=from,To=to,Identity=Identity.Trim(),Text=Text.Trim(),EventIds=eventIds.ToArray(),Provider=Provider.Trim(),Origin=Origin.Trim(),Computer=Computer.Trim(),Protocol=Protocol.Trim(),Failure=Failure.Trim(),Category=Enum.TryParse<EventCategory>(Category,out var cat)?cat:null,MaximumLevel=Severity switch {"Critical + Error"=>2,"Warning and higher"=>3,"Information and higher"=>4,_=>null}};
    }
    IReadOnlyList<LogSource> Sources()=>Section=="Imported EVTX Files"?Imported.ToArray():Computers.SelectMany(c=>Channels.Select(p=>new LogSource(c,p))).Concat(Section is "All Events" or "Overview" or "User Investigation"?Imported:[]).DistinctBy(s=>s.Key).ToArray();
    public async Task NavigateAsync(string target)
    {
        if(target=="Saved Searches")return;
        if(target is "Security" or "System" or "Application")Channels=[target];
        else if(target is "User Investigation" or "Live Monitoring")Channels=["Security"];
        else if(target is "All Events" or "Overview")Channels=["Security","System","Application"];
        SourcesChanged();if(target!="Custom Event Channels")await SearchAsync();
    }
    public async Task SearchAsync(bool retainLive=false)
    {
        if(closing)return;
        if(!retainLive&&live!=null)await StopWatchAsync();
        var myGeneration=++generation;Cancel();QuerySpec spec;IReadOnlyList<LogSource> sources;
        try{spec=Spec();sources=Sources();if(sources.Count==0)throw new InvalidOperationException("Choose at least one source or import an EVTX file.");if(sources.Count>32)throw new InvalidOperationException("Choose at most 32 combined computer/channel and EVTX sources. Use Sources to unload imported files or narrow channels.");}
        catch(Exception e){Status=e.Message;return;}
        if(query!=null){var retired=query;_ = Task.Run(retired.Dispose);}
        query=queryFactory(sources,spec);Changed(nameof(HasQuery));pageOffset=0;queryCancellation=new();Events.Clear();keys.Clear();evicted=0;retainedBytes=0;Selected=null;SourceNotices.Clear();HasMore=false;
        Notice=spec.Mode;await ReadAsync(myGeneration,query,queryCancellation.Token);
    }
    public async Task LoadMoreAsync()
    {
        if(IsBusy||query==null||!HasMore||IsWatching)return;
        if(liveDisplay){pageOffset=0;liveDisplay=false;}else if(query.Store.Count>pageOffset+Events.Count||Events.Count>0)pageOffset+=Events.Count;
        queryCancellation?.Dispose();queryCancellation=new();await ReadAsync(generation,query,queryCancellation.Token);
    }
    public async Task PreviousPageAsync()
    {
        if(IsBusy||query==null||pageOffset==0||IsWatching)return;pageOffset=Math.Max(0,pageOffset-500);queryCancellation?.Dispose();queryCancellation=new();await ReadAsync(generation,query,queryCancellation.Token);
    }
    public async Task CompleteSearchAsync()
    {
        if(IsBusy||query==null)return;if(IsWatching)await StopWatchAsync();var active=query;var version=generation;queryCancellation?.Dispose();queryCancellation=new();IsBusy=true;
        try
        {
            var progress=new Progress<SearchProgress>(p=>{if(version==generation)Status=$"Scanning all history · {p.Scanned:N0} examined · {p.Matches:N0} matches cached · Cancel retains completed batches";});
            await active.CompleteAsync(progress,queryCancellation.Token);if(version==generation){Status=active.Issues.Count==0?$"Scan finished: {active.Store.Count:N0} matching events cached.":"Scan finished with source errors; inspect Source notices before drawing conclusions.";}
        }
        catch(OperationCanceledException){if(version==generation)Status="Scan cancelled; cached results remain available. The investigation is partial.";}
        catch(Exception e){if(version==generation)Status=NativeQuery.Friendly(e);}
        finally{if(version==generation){IsBusy=false;UpdatePageState(active);}}
    }
    public void UpdatePageState(QuerySession active)
    {
        if(!ReferenceEquals(query,active))return;foreach(var issue in active.Issues)AddIssue(issue);HasMore=active.Store.Count>pageOffset+Events.Count||!active.Completed;Changed(nameof(PageStatus));Changed(nameof(CanPrevious));
    }
    public async Task RunPlanAsync(InvestigationPlan plan)
    {
        activePlan=plan;selectors=plan.Selectors;Section="User Investigation";Channels=plan.Channels.ToList();Ids="";Provider="";Category="All categories";Severity="All severities";Origin="";Computer="";Protocol="";Failure="";Text="";
        if(plan.Name is not ("Account lockout" or "Failed sign-ins"))Identity="";
        Changed(nameof(PlanStatus));Changed(nameof(HasPlan));SourcesChanged();await SearchAsync();Notice="Guided investigation: "+plan.Name+". Select Guided investigations to review the question, coverage and next checks. Provider-scoped selections are active.";
    }
    public void ClearPlan(){activePlan=null;selectors=[];Changed(nameof(PlanStatus));Changed(nameof(HasPlan));Notice="Guided event selections cleared. Other filters remain unchanged.";}
    public async Task ExportAllAsync(string path,bool json)
    {
        if(IsBusy||query==null){Status="Run a query and wait for its current page before exporting all matches.";return;}
        var active=query;int version=generation;queryCancellation?.Dispose();queryCancellation=new();IsBusy=true;
        try
        {
            var token=queryCancellation.Token;var progress=new Progress<SearchProgress>(p=>{if(version==generation)Status=$"Exporting all matches: {p.Scanned:N0} examined; {p.Matches:N0} cached";});
            long count=await Task.Run(()=>Exporter.WriteAllAsync(path,active.ReadAllAsync(progress,token),json,active.Filter,active.Sources,()=>new(active.Scanned,active.Store.Count,active.Completed,active.Issues),token),token);
            if(version==generation)Status=$"Exported {count:N0} matching records. "+(active.Issues.Count==0?"Accessible query exhausted; review coverage metadata.":"PARTIAL SOURCE COVERAGE: source errors are recorded in the report.");
        }
        catch(OperationCanceledException){if(version==generation)Status="Export cancelled. Existing destination was preserved; cached results remain available.";}
        catch(Exception e){if(version==generation)Status="Export did not finish: "+NativeQuery.Friendly(e);}
        finally{if(version==generation){IsBusy=false;UpdatePageState(active);}}
    }
    public async Task<string?> AnalyzeAllAsync(InvestigationPlan plan)
    {
        if(IsBusy||query==null||activePlan!=plan)return "Run this guided investigation first, then select Analyze all matches.";
        var active=query;int version=generation;queryCancellation?.Dispose();queryCancellation=new();IsBusy=true;
        try
        {
            var token=queryCancellation.Token;var progress=new Progress<SearchProgress>(p=>{if(version==generation)Status=$"Analyzing all matching evidence: {p.Scanned:N0} examined; {p.Matches:N0} cached";});
            string result=await Task.Run(async()=>{var evidence=new InvestigationEvidence(plan);await foreach(var item in active.ReadAllAsync(progress,token))evidence.Add(item);return evidence.Report(active.Sources,active.Filter,new(active.Scanned,active.Store.Count,active.Completed,active.Issues));},token);
            if(version==generation)Status="All-match evidence summary finished. Review completeness and source errors in the summary.";return result;
        }
        catch(OperationCanceledException){Status="Analysis cancelled; cached evidence remains available.";return "Analysis cancelled. No complete finding was produced.";}
        finally{if(version==generation){IsBusy=false;UpdatePageState(active);}}
    }
    public async Task RunExternalAsync(Func<CancellationToken,Task> action,string label)
    {
        if(IsBusy)return;int version=generation;queryCancellation?.Dispose();queryCancellation=new();IsBusy=true;Status=label;
        try{await action(queryCancellation.Token).WaitAsync(queryCancellation.Token);if(version==generation)Status="Native EVTX exported. Only native filters were applied; identity/text filters were not applied.";}
        catch(OperationCanceledException){if(version==generation)Status="Cancellation requested; native cleanup may still be finishing.";}
        catch(Exception e){if(version==generation)Status=NativeQuery.Friendly(e);}
        finally{if(version==generation)IsBusy=false;}
    }
    async Task ReadAsync(int version,QuerySession active,CancellationToken token)
    {
        IsBusy=true;Status="Reading event sources…";
        try
        {
            if(active.Store.Count<pageOffset+500&&!active.Completed)
            {
                var work=active.ReadPageAsync(500,token);await work.WaitAsync(TimeSpan.FromSeconds(30),token);
            }
            var page=await Task.Run(()=>active.Store.ReadPage(pageOffset,500,token),token);
            if(version!=generation||closing)return;
            liveDisplay=false;Events.Clear();keys.Clear();evicted=0;retainedBytes=0;
            foreach(var item in page)AddEvent(item,false);
            UpdatePageState(active);Status=$"{active.Scanned:N0} records examined · {active.Store.Count:N0} matches cached. Displaying one page.";RefreshStats();
        }
        catch(OperationCanceledException){if(version==generation)Status="Search cancelled. Already loaded results remain.";}
        catch(TimeoutException){queryCancellation?.Cancel();if(version==generation)Status="Native query deadline reached. Some RPC calls may still be finishing; cancel/retry or check remote connectivity.";}
        catch(Exception e){if(version==generation)Status=NativeQuery.Friendly(e);}
        finally{if(version==generation)IsBusy=false;}
    }
    public void Cancel(){queryCancellation?.Cancel();Status="Cancellation requested";}
    public async Task ToggleWatchAsync()
    {
        if(live!=null){await StopWatchAsync();return;}
        try
        {
            var spec=Spec();var sources=Sources().Where(s=>!s.IsFile).ToArray();if(sources.Length==0)throw new InvalidOperationException("EVTX files are static. Select a local or remote live channel.");if(sources.Length>16)throw new InvalidOperationException("Live monitoring supports at most 16 computer/channel combinations. Narrow sources before starting.");
            IsBusy=true;var fingerprint=System.Text.Json.JsonSerializer.Serialize(spec with{From=null,To=null});var monitor=new LiveMonitor(spec,checkpoints:fingerprint==lastLiveFilter?lastLivePoints:[]);lastLiveFilter=fingerprint;live=monitor;liveDisplay=true;Changed(nameof(PageStatus));Changed(nameof(CanPrevious));Changed(nameof(WatchLabel));Changed(nameof(IsWatching));
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try{await monitor.StartAsync(sources,timeout.Token).WaitAsync(TimeSpan.FromSeconds(25));}
            catch{try{await StopWatchAsync().WaitAsync(TimeSpan.FromSeconds(3));}catch(TimeoutException){}throw;}
            if(monitor.SourceCount==0){foreach(var issue in monitor.TakeIssues())AddIssue(issue);await StopWatchAsync();throw new InvalidOperationException("No live subscriptions could be started. See Source notices.");}
            Paused=false;LiveStatus=$"Watching {monitor.SourceCount} source(s) from {monitor.StartedUtc:O}. First start reads new events; a matching in-session checkpoint replays available later records.";
            Notice="Live filters are captured at Start. To apply changes, stop and start watching. Pause buffers up to 4,096 matches / 64 MB. Transient source errors retry automatically; overflow stops collection. Recover live replays available history from the last accepted checkpoint. Checkpoints are session-only; gaps stay visible.";
        }
        catch(Exception e){LiveStatus=NativeQuery.Friendly(e);}
        finally{IsBusy=false;}
    }
    public async Task StopWatchAsync()
    {
        var monitor=live;live=null;Changed(nameof(WatchLabel));Changed(nameof(IsWatching));
        if(monitor!=null)
        {
            try{await monitor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));LiveStatus=$"Stopped · {monitor.Received:N0} received · {monitor.Dropped:N0} unretained";}
            catch(TimeoutException){LiveStatus="Stop requested; a native RPC call is still finishing. Its resources will be released when it returns.";}
            lastLivePoints=monitor.CaptureCheckpoints();while(monitor.TryRead(out var pending))AddEvent(pending,true);RefreshStats();
            foreach(var issue in monitor.TakeIssues())AddIssue(issue);
        }
    }
    public void RecoverLive(){if(live==null)return;Paused=false;DrainLive();live.RecoverNow();LiveStatus="Recovery requested. Available retained history will replay; review Source notices for gaps.";}
    void DrainLive()
    {
        if(live==null||closing)return;foreach(var issue in live.TakeIssues())AddIssue(issue);
        if(Paused)return;
        bool changed=false;for(int count=0;count<150&&live.TryRead(out var e);count++){AddEvent(e,true);changed=true;if(Sound&&e.Category is EventCategory.Lockout or EventCategory.FailedAuthentication)NotifyFailure?.Invoke();}
        if(changed){RefreshStats();if(AutoScroll)ScrollNewest?.Invoke();}
        string state=live.CollectionStopped?"Collection stopped; inspect Source notices":live.FaultedSources>0?$"Degraded live ({live.FaultedSources} source errors)":"Live";
        LiveStatus=$"{state} · {live.SourceCount} source(s) · {live.Received:N0} received · {live.Dropped:N0} unretained · {live.Recoveries:N0} reattachments · {live.Duplicates:N0} replay duplicates suppressed · {evicted:N0} older rows evicted from the bounded display";
    }
    void AddEvent(EventItem e,bool newest)
    {
        e.IsLive=newest;
        if(!keys.Add(e.Key))
        {
            var existing=Events.FirstOrDefault(item=>item.Key==e.Key);
            if(existing!=null&&!existing.ContributingSources.Contains(e.Source))existing.ContributingSources.Add(e.Source);
            return;
        }
        e.ContributingSources.Add(e.Source);
        retainedBytes+=PayloadBytes(e);
        int lo=0,hi=Events.Count;while(lo<hi){int mid=(lo+hi)/2;if(Events[mid].Timestamp<e.Timestamp)hi=mid;else lo=mid+1;}Events.Insert(lo,e);
        while(Events.Count>ResultLimit||retainedBytes>PayloadBudget)
        {
            var removed=Events[^1];retainedBytes-=PayloadBytes(removed);keys.Remove(removed.Key);Events.RemoveAt(Events.Count-1);evicted++;
        }
    }
    static long PayloadBytes(EventItem e)=>e.EstimatedBytes;
    void AddIssue(SourceIssue issue){string message=issue.Source+": "+issue.Message;if(!SourceNotices.Contains(message)&&SourceNotices.Count<100)SourceNotices.Add(message);}
    void RefreshStats(){Changed(nameof(Stats));Changed(nameof(IdentityNotice));Changed(nameof(Origins));}
    public void RefreshForPreview()=>RefreshStats();
    public Task InvestigateLockoutAsync()=>RunPlanAsync(InvestigationPlans.All[0]);
    public void SaveSearch()
    {
        try{if(string.IsNullOrWhiteSpace(SearchName))throw new FormatException("Give the search a name.");var saved=new SavedSearch(SearchName.Trim(),Spec(),Range,Computers.ToArray(),Channels.ToArray());var old=Saved.FirstOrDefault(s=>s.Name.Equals(saved.Name,StringComparison.OrdinalIgnoreCase));if(old!=null)Saved.Remove(old);Saved.Add(saved);Persist();Status="Saved filter: "+saved.Name;}
        catch(Exception e){Status="Could not save filter: "+e.Message;}
    }
    public async Task ApplySavedAsync()
    {
        if(SelectedSaved==null)return;activePlan=null;selectors=SelectedSaved.Filter.Selectors;var s=SelectedSaved.Filter;Identity=s.Identity;Text=s.Text;Ids=string.Join(",",s.EventIds);Provider=s.Provider;Category=s.Category?.ToString()??"All categories";Origin=s.Origin;Computer=s.Computer;Protocol=s.Protocol;Failure=s.Failure;
        Severity=s.MaximumLevel switch {2=>"Critical + Error",3=>"Warning and higher",4=>"Information and higher",_=>"All severities"};Range=SelectedSaved.TimeRange;CustomFrom=s.From?.ToString("O")??"";CustomTo=s.To?.ToString("O")??"";
        if(SelectedSaved.Computers is {Length:>0} hosts)Computers=hosts.ToList();
        Channels=SelectedSaved.Channels is {Length:>0} channels?channels.ToList():["Security","System","Application"];
        Section="All Events";Changed(nameof(PlanStatus));Changed(nameof(HasPlan));SourcesChanged();await SearchAsync();
    }
    public void RemoveSaved(){if(SelectedSaved is {} s){Saved.Remove(s);Persist();}}
    public void Persist(){Settings=Settings with {Searches=Saved.ToList(),Computers=Computers};SettingsStore.Save(Settings);}
    public void SaveColumns(List<string> hidden,Dictionary<string,double> widths){Settings=Settings with {HiddenColumns=hidden,ColumnWidths=widths};Persist();}
    public async ValueTask DisposeAsync()
    {
        closing=true;generation++;timer.Stop();Cancel();await StopWatchAsync();var retired=query;query=null;
        if(retired!=null)try{await Task.Run(retired.Dispose).WaitAsync(TimeSpan.FromSeconds(2));}catch(TimeoutException){}
        queryCancellation?.Dispose();
    }
}
