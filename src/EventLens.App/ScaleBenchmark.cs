using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EventLens.Core;
using EventLens.Windows;

namespace EventLens.App;
internal static class ScaleBenchmark
{
    sealed class Cursor(LogSource source,int count,IncrementalHash expected) : IEventCursor
    {
        int position;public LogSource Source=>source;public EventItem? Head{get;set;}public bool End{get;set;}
        public EventItem? Read(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();if(position>=count){End=true;return null;}
            int id=++position;string xml=$"<Event><System><Provider Name='EventLens-Synthetic'/><EventID>1</EventID><EventRecordID>{id}</EventRecordID><TimeCreated SystemTime='2026-10-22T12:00:00Z'/><Channel>Fixture</Channel><Computer>SYNTHETIC</Computer></System><EventData><Data Name='Fixture'>Synthetic record {id}</Data></EventData></Event>";
            expected.AppendData(Encoding.UTF8.GetBytes(xml));return EventParser.Parse(xml,source:source.Key);
        }
        public void Dispose(){}
    }
    public static async Task<int> RunAsync(string path)
    {
        const int count=100000;using var expected=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);using var actual=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var query=new QuerySession([new("synthetic","Fixture")],new(){From=null},(s,_)=>new Cursor(s,count,expected));using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var timer=Stopwatch.StartNew();await query.CompleteAsync(null,timeout.Token);long captureMs=timer.ElapsedMilliseconds;long fileBytes=new FileInfo(query.Store.Path).Length;timer.Restart();int read=0;
        await foreach(var item in query.ReadAllAsync(token:timeout.Token)){read++;actual.AppendData(Encoding.UTF8.GetBytes(item.Xml));}
        bool equal=read==count&&expected.GetHashAndReset().SequenceEqual(actual.GetHashAndReset());
        var result=new{Date="2026-10-23",Fixture="100,000 synthetic events, not a native EVTX throughput claim",Expected=count,Actual=read,OrderedXmlMatches=equal,CaptureMs=captureMs,ReplayMs=timer.ElapsedMilliseconds,CacheBytes=fileBytes,ProcessPeakWorkingSetBytes=Process.GetCurrentProcess().PeakWorkingSet64,Reference="Bounded-memory cursor generation; complete disk capture and page replay; no Event Viewer UI timing comparison",Ok=equal};
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);await File.WriteAllTextAsync(path,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));return equal?0:1;
    }
}
