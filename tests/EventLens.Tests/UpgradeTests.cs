using System.IO.Compression;
using System.Text.Json;
using EventLens.Core;
using Xunit;

namespace EventLens.Tests;
public class UpgradeTests
{
    static EventItem Item(int id,string source="fixture")=>EventParser.Parse(CoreTests.Xml(4625,data:CoreTests.Data("TargetUserName","user")+CoreTests.Data("Status","0xC000006A"),record:id),source:source);
    [Fact]public void DiskCacheExceedsOldDisplayLimitAndPagesWithoutLosingDuplicatesProvenance()
    {
        string path;
        using(var store=new ResultStore())
        {
            path=store.Path;
            for(int start=0;start<26000;start+=500)store.AddRange(Enumerable.Range(start+1,500).Select(i=>Item(i)));
            store.AddRange([Item(1,"overlapping-file"),Item(26001)]);
            Assert.Equal(26001,store.Count);Assert.Equal(26001,Assert.Single(store.ReadPage(26000)).RecordId);
            Assert.Equal(new[]{"fixture","overlapping-file"},store.ReadPage(0,1)[0].ContributingSources);
            Assert.Equal(501,store.ReadPage(500,1)[0].RecordId);Assert.Equal(26000,store.ReadPage(25999,1)[0].RecordId);
        }
        Assert.False(File.Exists(path));
    }
    [Fact]public void CancelledCacheBatchRollsBackAndDoesNotAdvanceCount()
    {
        using var store=new ResultStore();using var cancel=new CancellationTokenSource();
        IEnumerable<EventItem> Values(){yield return Item(1);cancel.Cancel();yield return Item(2);}
        Assert.Throws<OperationCanceledException>(()=>store.AddRange(Values(),cancel.Token));Assert.Equal(0,store.Count);Assert.Empty(store.ReadPage(0));store.AddRange([Item(3)]);Assert.Single(store.ReadPage(0));
    }
    [Fact]public void CachePreservesVersionActivityAndUnicodePayload()
    {
        var e=EventParser.Parse(CoreTests.Xml(4625,data:CoreTests.Data("TargetUserName","Zoë")).Replace("<Level>","<Version>0</Version><Correlation ActivityID='{123}'/><Execution ProcessID='42' ThreadID='7'/><Level>"));
        using var store=new ResultStore();store.AddRange([e]);var restored=store.ReadPage(0)[0];Assert.Equal("{123}",restored.ActivityId);Assert.Equal("42",restored.ProcessId);Assert.Equal("Zoë",restored.User);Assert.Equal(e.Xml,restored.Xml);
    }
    [Fact]public void LargePayloadPagesStayBoundedWithoutSkippingEvidence()
    {
        using var store=new ResultStore();string payload=new('x',120000);
        for(int i=1;i<=80;i++)store.AddRange([EventParser.Parse(CoreTests.Xml(1,"Fixture",CoreTests.Data("Large",payload),record:i))]);
        var first=store.ReadPage(0,500);Assert.InRange(first.Count,1,79);long seen=0;
        while(seen<store.Count){var page=store.ReadPage(seen,500);Assert.NotEmpty(page);Assert.Equal(seen+1,page[0].RecordId);seen+=page.Count;}
        Assert.Equal(80,seen);
    }
    [Fact]public void FutureAuthenticationVersionDoesNotInheritOldCodeMeaning()
    {
        var e=EventParser.Parse(CoreTests.Xml(4625,data:CoreTests.Data("Status","0xC000006A")).Replace("<Level>","<Version>99</Version><Level>"),"Future provider message");
        Assert.False(e.Known);Assert.Contains("not been reviewed",e.Explanation);Assert.Empty(e.Failure);Assert.False(e.Guidance.HasSpecificGuide);Assert.Contains(e.Codes,c=>c.Context.Contains("Candidate only"));
    }
    [Theory][InlineData(4624,2)][InlineData(4768,2)][InlineData(4769,2)]
    public void ReviewedVersionsRetainInterpretation(int id,int version)
    {
        var e=EventParser.Parse(CoreTests.Xml(id,data:CoreTests.Data("Status","0x0")).Replace("<Level>",$"<Version>{version}</Version><Level>"));Assert.False(SchemaSupport.Unreviewed(e));Assert.True(e.Known);
    }
    [Fact]public void GuidedPlansScopeNumericIdsToProviders()
    {
        var plan=InvestigationPlans.All.Single(p=>p.Name=="Application crash");var filter=new QuerySpec{From=null,Selectors=plan.Selectors};
        Assert.True(EventFilter.Matches(EventParser.Parse(CoreTests.Xml(1000,"Application Error")),filter));Assert.False(EventFilter.Matches(EventParser.Parse(CoreTests.Xml(1000,"Unrelated vendor")),filter));Assert.Equal(5,InvestigationPlans.All.Count);
        Assert.Contains("Partial",InvestigationPlans.Describe(plan,[],[new("host","Application")],false,[new("host","Access denied")]));
    }
    [Fact]public void PlainWordsUsesRecordedFactsWithoutInventingAProcessOrCause()
    {
        var e=EventParser.Parse(CoreTests.Xml(4625,data:CoreTests.Data("TargetUserName","Zoë")+CoreTests.Data("Status","0xC000006A")+CoreTests.Data("IpAddress","192.0.2.14")));
        Assert.StartsWith("Windows recorded an unsuccessful",e.Explanation);Assert.Contains("Zoë",e.Explanation);Assert.Contains("192.0.2.14",e.Explanation);Assert.Contains("does not identify the application",e.Explanation);
    }
    [Fact]public void AllMatchInvestigationSummaryNamesAbsentEvidenceAndSourceErrors()
    {
        var plan=InvestigationPlans.All[0];var evidence=new InvestigationEvidence(plan);evidence.Add(Item(1));
        string report=evidence.Report([new("host","Security")],new(){From=null},new(1,1,true,[new("other DC","Access denied")]));
        Assert.Contains("1 unique matching events",report);Assert.Contains("4740",report);Assert.Contains("PARTIAL",report);Assert.Contains("other DC: Access denied",report);Assert.Contains("not a root cause",report);
    }
    [Theory][InlineData("Windows Error Reporting",1001)][InlineData(".NET Runtime",1026)][InlineData("Application Hang",1002)]
    public void ApplicationEvidenceGuidanceStaysProviderScoped(string provider,int id)
    {
        Assert.True(EventParser.Parse(CoreTests.Xml(id,provider)).Guidance.HasSpecificGuide);Assert.False(EventParser.Parse(CoreTests.Xml(id,"Unrelated")).Guidance.HasSpecificGuide);
    }
    static async IAsyncEnumerable<EventItem> Items(int count,[System.Runtime.CompilerServices.EnumeratorCancellation]CancellationToken token=default)
    {
        for(int i=1;i<=count;i++){token.ThrowIfCancellationRequested();yield return Item(i);if(i%100==0)await Task.Yield();}
    }
    [Fact]public async Task FullJsonAndCsvBundleIncludeCompletenessAndEveryRecord()
    {
        string directory=Path.Combine(Path.GetTempPath(),"eventlens-export-test-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try
        {
            var filter=new QuerySpec{From=null,To=DateTimeOffset.UtcNow};LogSource[] sources=[new("host","Security")];
            var count=await Exporter.WriteAllAsync(Path.Combine(directory,"all.json"),Items(1200),true,filter,sources,()=>new(1500,1200,true,[]));Assert.Equal(1200,count);
            using var json=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory,"all.json")));Assert.Equal(1200,json.RootElement.GetProperty("Events").GetArrayLength());Assert.True(json.RootElement.GetProperty("Investigation").GetProperty("Complete").GetBoolean());
            await Exporter.WriteAllAsync(Path.Combine(directory,"all.zip"),Items(3),false,filter,sources,()=>new(3,3,true,[new("denied","Access denied")]));
            using var zip=ZipFile.OpenRead(Path.Combine(directory,"all.zip"));Assert.NotNull(zip.GetEntry("events.csv"));using var meta=JsonDocument.Parse(zip.GetEntry("investigation.json")!.Open());Assert.False(meta.RootElement.GetProperty("Complete").GetBoolean());Assert.Single(meta.RootElement.GetProperty("SourceIssues").EnumerateArray());
        }
        finally{Directory.Delete(directory,true);}
    }
    [Fact]public async Task CancelledFullExportPreservesDestinationAndRemovesTemporaryOutput()
    {
        string directory=Path.Combine(Path.GetTempPath(),"eventlens-cancel-test-"+Guid.NewGuid());Directory.CreateDirectory(directory);string path=Path.Combine(directory,"all.json");await File.WriteAllTextAsync(path,"original");using var cancel=new CancellationTokenSource();
        async IAsyncEnumerable<EventItem> Values(){yield return Item(1);await Task.Yield();cancel.Cancel();cancel.Token.ThrowIfCancellationRequested();yield return Item(2);}
        try{await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Exporter.WriteAllAsync(path,Values(),true,new(),[],()=>new(1,1,false,[]),cancel.Token));Assert.Equal("original",await File.ReadAllTextAsync(path));Assert.Single(Directory.GetFiles(directory));}finally{Directory.Delete(directory,true);}
    }
    [Fact]public async Task FullReportRejectsEvtxDestination()=>await Assert.ThrowsAsync<ArgumentException>(()=>Exporter.WriteAllAsync("protected.evtx",Items(1),true,new(),[],()=>new(1,1,true,[])));
}
