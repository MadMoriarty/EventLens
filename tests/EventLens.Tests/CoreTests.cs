using EventLens.Core;
using System.Diagnostics;
using System.Text.Json;
using Xunit;
namespace EventLens.Tests;

public class CoreTests
{
    public static string Xml(int id,string provider=Interpreter.SecurityProvider,string data="",string time="2026-10-22T12:00:00.000Z",long record=1)=>$"<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='{provider}'/><EventID>{id}</EventID><Level>0</Level><Keywords>0x8020000000000000</Keywords><TimeCreated SystemTime='{time}'/><EventRecordID>{record}</EventRecordID><Channel>Security</Channel><Computer>DC01.contoso.test</Computer></System><EventData>{data}</EventData></Event>";
    public static string Data(string key,string value)=>$"<Data Name='{key}'>{System.Security.SecurityElement.Escape(value)}</Data>";
    static EventItem Auth(int id,string username="jsmith",string domain="CONTOSO",string sid="S-1-5-21-1-2-3-1001",string status="0x0",string sub="0x0",string time="2026-10-22T12:00:00Z",long record=1)=>EventParser.Parse(Xml(id,data:Data("TargetUserName",username)+Data("TargetDomainName",domain)+Data("TargetUserSid",sid)+Data("Status",status)+Data("SubStatus",sub)+Data("IpAddress","10.0.0.24"),time:time,record:record));
    [Fact]public void ParsesUnicodeAndUtc(){var e=Auth(4624,"Zoë");Assert.Equal("Zoë",e.Identities[0].Name);Assert.Equal(TimeSpan.Zero,e.Timestamp!.Value.Offset);Assert.Contains("xmlns",e.Xml);Assert.Equal(EventCategory.SuccessfulAuthentication,e.Category);}
    [Theory][InlineData("0xC000006A","Incorrect password")][InlineData("0xc0000064","Username does not exist")][InlineData("0xC0000234","Account locked")][InlineData("0xC0000072","Account disabled")][InlineData("0xC000006F","Outside permitted logon hours")][InlineData("0xC000015B","Requested logon type is not granted")]
    public void FailedLogonUsesSubstatus(string code,string expected){var e=Auth(4625,status:"0xC000006D",sub:code);Assert.Equal(expected,e.Failure);Assert.Contains(expected,e.Title);Assert.Equal(EventCategory.FailedAuthentication,e.Category);}
    [Fact]public void FallsBackToStatusWhenSubstatusZero(){var e=Auth(4625,status:"0xC0000234");Assert.Equal("Account locked",e.Failure);Assert.NotEqual(EventCategory.Lockout,e.Category);}
    [Fact]public void AuditLevelZeroIsNotAnErrorSeverity(){Assert.False(EventFilter.Matches(Auth(4625),new(){From=null,MaximumLevel=2}));Assert.True(EventFilter.Matches(Auth(4625),new(){From=null,Category=EventCategory.FailedAuthentication}));}
    [Fact]public void OriginIncludesWorkstationAndNormalizedIp(){var e=EventParser.Parse(Xml(4625,data:Data("WorkstationName","WS-024")+Data("IpAddress","::ffff:192.0.2.24")));Assert.Equal("WS-024 (192.0.2.24)",e.Origin);}
    [Fact]public void SuccessfulEventIsNotClassifiedAsFailureFromData(){var e=Auth(4624,status:"0xC000006A");Assert.Equal(EventCategory.SuccessfulAuthentication,e.Category);Assert.Empty(e.Failure);}
    [Fact]public void SameIdFromOtherProviderIsNotSecurityEvent(){var e=EventParser.Parse(Xml(4625,"Unrelated provider",Data("Status","0xC000006A")),"Original provider text");Assert.False(e.Known);Assert.Equal("Original provider text",e.Title);Assert.Equal(EventCategory.Other,e.Category);}
    [Fact]public void KerberosCodesAreNotNtStatus(){var e=Auth(4771,status:"0x18");Assert.Contains("preauthentication",e.Failure);Assert.DoesNotContain("Unmapped Windows",e.Title);Assert.Equal(EventCategory.FailedAuthentication,e.Category);}
    [Theory][InlineData(4768,"0x0",EventCategory.SuccessfulAuthentication)][InlineData(4769,"0x6",EventCategory.FailedAuthentication)][InlineData(4776,"0xC000006A",EventCategory.FailedAuthentication)]
    public void ProtocolOutcomeIsPayloadDependent(int id,string status,EventCategory category)=>Assert.Equal(category,Auth(id,status:status).Category);
    [Fact]public void UnknownStatusIsPreserved(){var e=Auth(4625,status:"0xDEADBEEF");Assert.Contains("DEADBEEF",e.Failure);Assert.Contains("0xDEADBEEF",e.Xml);}
    [Theory][InlineData(4720,EventCategory.AccountChange)][InlineData(4726,EventCategory.AccountChange)][InlineData(4723,EventCategory.PasswordChange)][InlineData(4724,EventCategory.PasswordChange)][InlineData(4732,EventCategory.GroupChange)][InlineData(4719,EventCategory.PolicyChange)][InlineData(4740,EventCategory.Lockout)]
    public void KnownCategories(int id,EventCategory category)=>Assert.Equal(category,Auth(id).Category);
    [Fact]public void PasswordAttemptsDoNotAssumeSuccess(){var e=Auth(4724);Assert.Contains("attempt",e.Title);Assert.Contains("Successful",e.Title);var failure=EventParser.Parse(Xml(4724).Replace("0x8020000000000000","0x8010000000000000"));Assert.Contains("Failed",failure.Title);}
    [Theory][InlineData("jsmith",true)][InlineData("contoso\\JSMITH",true)][InlineData("OTHER\\jsmith",false)][InlineData("S-1-5-21-1-2-3-1001",true)][InlineData("smith",false)][InlineData("S-1-5-21-1-2-3-1002",false)]
    public void MatchesExactIdentityAndScope(string query,bool expected)=>Assert.Equal(expected,IdentityQuery.Parse(query).Matches(Auth(4624)));
    [Fact]public void UpnDomainIsNotGuessedFromNetbios(){Assert.False(IdentityQuery.Parse("jsmith@contoso.test").Matches(Auth(4624)));var e=Auth(4624,"jsmith@contoso.test","");Assert.True(IdentityQuery.Parse("jsmith@contoso.test").Matches(e));}
    [Fact]public void SameNameAcrossDomainsIsAmbiguous(){var entries=new[]{Auth(4624),Auth(4624,domain:"OTHER",sid:"S-1-5-21-4-5-6-1001")};Assert.Contains("Ambiguous",IdentityQuery.Parse("jsmith").AmbiguityNotice(entries));Assert.Equal(2,Investigator.Group(entries).Count);}
    [Fact]public void ActorAndTargetRemainSeparate(){var e=EventParser.Parse(Xml(4724,data:Data("SubjectUserName","admin")+Data("SubjectDomainName","CONTOSO")+Data("TargetUserName","jsmith")+Data("TargetDomainName","CONTOSO")));Assert.Equal("Subject",IdentityQuery.Parse("admin").Matching(e).Single().Role);Assert.Equal("Target",IdentityQuery.Parse("jsmith").Matching(e).Single().Role);}
    [Fact]public void DuplicatePayloadFieldsAndUserDataPreserved(){var e=EventParser.Parse(Xml(1,data:Data("Name","A")+Data("Name","B")));Assert.Equal("B",e.Data["Name[2]"]);var custom=EventParser.Parse("<Event><System><EventID>2</EventID></System><UserData><Payload><AccountName>jsmith</AccountName></Payload></UserData></Event>");Assert.True(IdentityQuery.Parse("jsmith").Matches(custom));}
    [Theory][InlineData("<Event>")][InlineData("<Other/>")][InlineData("<Event/>")][InlineData("<!DOCTYPE Event [<!ENTITY x SYSTEM 'file:///C:/secret'>]><Event>&x;</Event>")]
    public void MalformedOrDtdInputRejected(string xml)=>Assert.ThrowsAny<Exception>(()=>EventParser.Parse(xml));
    [Fact]public void MissingTimestampIsExplicit(){var e=EventParser.Parse(Xml(1,time:"garbage"));Assert.Null(e.Timestamp);Assert.Equal("Time unavailable",e.TimestampDisplay);Assert.False(EventFilter.Matches(e,new(){From=DateTimeOffset.UtcNow.AddDays(-1)}));}
    [Fact]public void TimezonesNormalizeToSameInstant(){var a=Auth(4624,time:"2026-10-22T08:00:00-04:00");var b=Auth(4624);Assert.Equal(a.Timestamp,b.Timestamp);}
    [Fact]public void AllFiltersCombine(){var e=Auth(4625,status:"0xC000006A");var spec=new QuerySpec{From=DateTimeOffset.Parse("2026-10-22T00:00Z"),To=DateTimeOffset.Parse("2026-10-23T00:00Z"),Identity="CONTOSO\\jsmith",EventIds=[4625],Category=EventCategory.FailedAuthentication,Text="incorrect",Origin="10.0.0.24",Computer="dc01",Provider=Interpreter.SecurityProvider};Assert.True(EventFilter.Matches(e,spec));Assert.False(EventFilter.Matches(e,spec with {Identity="OTHER\\jsmith"}));Assert.False(EventFilter.Matches(e,spec with {EventIds=[4740]}));}
    [Fact]public void GroupingDoesNotMergeDifferentReasons(){var groups=Investigator.Group([Auth(4625,status:"0xC000006A"),Auth(4625,status:"0xC000006A",record:2),Auth(4625,status:"0xC0000072",record:3)]);Assert.Equal(2,groups.Count);Assert.Equal(2,groups[0].Count);Assert.Equal(2,groups[0].Items.Count);}
    [Fact]public void LockoutCorrelationKeepsEvidenceAndAvoidsCausalClaims(){var events=new[]{Auth(4625,status:"0xC000006A",time:"2026-10-22T11:59Z"),Auth(4740,time:"2026-10-22T12:00Z",record:2),Auth(4767,time:"2026-10-22T12:02Z",record:3),Auth(4624,time:"2026-10-22T12:03Z",record:4)};var finding=Assert.Single(Investigator.Correlate(events));Assert.Equal(4,finding.Events.Count);Assert.Contains("does not establish causation",finding.Evidence);}
    [Fact]public void DoesNotCorrelateDifferentSids(){var events=new[]{Auth(4625,status:"0xC000006A"),Auth(4740,sid:"S-1-5-21-9-9-9-1001",record:2)};Assert.Single(Investigator.Correlate(events));Assert.Single(Investigator.Correlate(events)[0].Events);}
    [Fact]public void Task201RequiresFailureResult(){var success=EventParser.Parse(Xml(201,"Microsoft-Windows-TaskScheduler",Data("ResultCode","0")));Assert.False(success.Known);var failed=EventParser.Parse(Xml(201,"Microsoft-Windows-TaskScheduler",Data("ResultCode","2147942402")));Assert.Equal(EventCategory.ScheduledTask,failed.Category);}
    [Fact]public void OverlappingExportsDoNotDoubleCountEvidence()
    {
        var xml=Xml(4625,data:Data("TargetUserName","jsmith")+Data("Status","0xC000006A"));
        var a=EventParser.Parse(xml,source:"File A");var b=EventParser.Parse(xml,source:"File B");Assert.Equal(a.Key,b.Key);Assert.Equal(1,Assert.Single(Investigator.Group([a,b])).Count);
    }
    [Fact]public async Task JsonAndCsvPreserveOriginalXmlAndEscapeFormulaCells()
    {
        var directory=Path.Combine(Path.GetTempPath(),"eventlens-test-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try{var e=EventParser.Parse(Xml(1,"Unmapped"),"=HYPERLINK(\"bad\")");await Exporter.WriteAsync(Path.Combine(directory,"a.csv"),[e],false);var csv=await File.ReadAllTextAsync(Path.Combine(directory,"a.csv"));Assert.Contains("'=HYPERLINK",csv);await Exporter.WriteAsync(Path.Combine(directory,"a.json"),[e],true);using var json=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory,"a.json")));Assert.Equal(e.Xml,json.RootElement.GetProperty("Events")[0].GetProperty("Xml").GetString());}finally{Directory.Delete(directory,true);}
    }
    [Fact]public async Task ReportExportCannotOverwriteEvtx()
    {
        string path=Path.Combine(Path.GetTempPath(),"eventlens-protected-"+Guid.NewGuid()+".evtx");await File.WriteAllTextAsync(path,"original log bytes");
        try{await Assert.ThrowsAsync<ArgumentException>(()=>Exporter.WriteAsync(path,[Auth(4624)],true));Assert.Equal("original log bytes",await File.ReadAllTextAsync(path));}finally{File.Delete(path);}
    }
    [Fact]public void SyntheticSearchAndGroupingPerformance()
    {
        var sample=Auth(4625,status:"0xC000006A");var watch=Stopwatch.StartNew();int matches=0;
        var filter=new QuerySpec{From=null,Identity="CONTOSO\\jsmith",Text="password"};for(int i=0;i<200_000;i++)if(EventFilter.Matches(sample,filter))matches++;
        Assert.Equal(200_000,matches);Assert.True(watch.Elapsed<TimeSpan.FromSeconds(30),$"200k filtering took {watch.Elapsed}");
        var groups=Investigator.Group(Enumerable.Range(0,100_000).Select(i=>new EventItem{RecordId=i,Timestamp=sample.Timestamp,Id=4625,Provider=sample.Provider,Computer=sample.Computer,Identities=sample.Identities,Category=sample.Category,Failure=sample.Failure,Data=sample.Data}));Assert.Equal(100_000,Assert.Single(groups).Count);
    }
}
