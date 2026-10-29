using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Text.Json;
using EventLens.Core;
using EventLens.Windows;
using System.Security.Cryptography;
using System.Text;
namespace EventLens.App;

internal static class Diagnostic
{
    public static async Task<int> RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);var results=new Dictionary<string,object>();bool ok=true;
        try
        {
            var timer=Stopwatch.StartNew();var total=0;
            foreach(var channel in new[]{"System","Application","Security"})
            {
                using var query=new QuerySession([new("localhost",channel)],new(){From=DateTimeOffset.UtcNow.AddDays(-7)});
                using var token=new CancellationTokenSource(TimeSpan.FromSeconds(15));var page=await query.ReadPageAsync(20,token.Token).WaitAsync(TimeSpan.FromSeconds(20));total+=page.Events.Count;
                results[channel]=new{Count=page.Events.Count,Issues=page.Issues.Select(i=>i.Message),RawXmlPreserved=page.Events.All(e=>e.Xml.Contains("<Event")),UtcTimes=page.Events.All(e=>e.Timestamp?.Offset==TimeSpan.Zero)};
                if(channel=="System"&&page.Events.Count>0)
                {
                    var path=Path.Combine(directory,"system-subset.evtx");if(File.Exists(path))File.Delete(path);
                    using var session=new EventLogSession();var lowest=page.Events.Min(e=>e.RecordId);
                    session.ExportLog("System",PathType.LogName,$"*[System[EventRecordID >= {lowest}]]",path);
                    using var fileQuery=new QuerySession([new("offline",path,true)],new(){From=null});var imported=await fileQuery.ReadPageAsync(20,token.Token);
                    results["EVTX"]=new{Records=imported.Events.Count,Issues=imported.Issues.Select(i=>i.Message),ReadOnly=true};ok&=imported.Events.Count>0;
                    await Exporter.WriteAsync(Path.Combine(directory,"subset.json"),page.Events,true);await Exporter.WriteAsync(Path.Combine(directory,"subset.csv"),page.Events,false);results["Exports"]=true;
                    var nativePath=Path.Combine(directory,"native-export.evtx");var captured=new QuerySpec{From=DateTimeOffset.UtcNow.AddDays(-7),To=DateTimeOffset.UtcNow};
                    await NativeExport.WriteAsync(new("localhost","System"),captured,nativePath,token.Token);
                    long expected=0;var nativeTimer=Stopwatch.StartNew();using var nativeHash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    using(var reader=new EventLogReader(new EventLogQuery(nativePath,PathType.FilePath,NativeQuery.Build(captured)){ReverseDirection=true}))
                    {
                        while(true){using var record=reader.ReadEvent();if(record==null)break;expected++;nativeHash.AppendData(Encoding.UTF8.GetBytes(record.ToXml()));}
                    }
                    nativeTimer.Stop();var expectedHash=Convert.ToHexString(nativeHash.GetHashAndReset());
                    using var measured=new QuerySession([new("offline",nativePath,true)],captured);var measuredTimer=Stopwatch.StartNew();
                    using var benchmarkCancel=new CancellationTokenSource(TimeSpan.FromMinutes(2));await measured.CompleteAsync(null,benchmarkCancel.Token);using var actualHash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long actual=0;
                    await foreach(var item in measured.ReadAllAsync(token:benchmarkCancel.Token)){actual++;actualHash.AppendData(Encoding.UTF8.GetBytes(item.Xml));}
                    measuredTimer.Stop();bool equal=actual==expected&&Convert.ToHexString(actualHash.GetHashAndReset())==expectedHash;ok&=equal;
                    var report=Path.Combine(directory,"all-matches.json");long exported=await Exporter.WriteAllAsync(report,measured.ReadAllAsync(token:benchmarkCancel.Token),true,measured.Filter,measured.Sources,()=>new(measured.Scanned,measured.Store.Count,measured.Completed,measured.Issues),benchmarkCancel.Token);ok&=exported==expected;
                    results["NativeReferenceComparison"]=new{Expected=expected,Actual=actual,Exported=exported,OrderedXmlMatches=equal,NativeReadMs=nativeTimer.ElapsedMilliseconds,EventLensCaptureAndReplayMs=measuredTimer.ElapsedMilliseconds,Scope="Same immutable EVTX snapshot and query. Reference reads XML only; EventLens also interprets and disk-caches. This is not an Event Viewer UI speed benchmark."};
                }
            }
            using(var bad=new QuerySession([new("localhost","EventLens-Nonexistent-Channel-For-Test")],new()))
            {
                var page=await bad.ReadPageAsync(10,CancellationToken.None);results["InaccessibleChannelHandled"]=page.Issues.Count>0;ok&=page.Issues.Count>0;
            }
            await using(var watch=new LiveMonitor(new(){From=null}))
            {
                await watch.StartAsync([new("localhost","System"),new("localhost","Application")],CancellationToken.None);await Task.Delay(5000);
                int delivered=0;while(watch.TryRead(out _))delivered++;
                results["Live"]=new{Attached=watch.ActiveSourceCount,Delivered=delivered,Dropped=watch.Dropped,Issues=watch.TakeIssues().Select(i=>i.Message),Coverage=delivered>0?"Natural local event delivery observed":"Subscription attach/dispose exercised; no natural event delivery observed during five seconds"};ok&=watch.ActiveSourceCount>0;
            }
            results["ElapsedMs"]=timer.ElapsedMilliseconds;results["RemoteValidated"]=false;results["DomainControllerValidated"]=false;results["RecordsRead"]=total;ok&=total>0;
        }
        catch(Exception e){results["Error"]=e.ToString();ok=false;}
        results["Ok"]=ok;await File.WriteAllTextAsync(Path.Combine(directory,"diagnostic-results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));return ok?0:1;
    }
}
