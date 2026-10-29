using EventLens.Core;
using System.Text.Json;
using Xunit;
namespace EventLens.Tests;

public class TroubleshootingTests
{
    static EventItem Event(int id,string provider=Interpreter.SecurityProvider,string data="")=>EventParser.Parse(CoreTests.Xml(id,provider,data));
    [Fact]public void AllThreeSourcesAreAvailableOnEveryEvent()
    {
        foreach(var item in new[]{Event(4625),Event(999,"Unknown Provider"),Event(41,"Microsoft-Windows-Kernel-Power")})
        {
            var sources=item.Guidance.References.Select(r=>r.Source).Distinct().Order().ToArray();Assert.Equal(new[]{"EventSentry","Microsoft","Ultimate Windows Security"},sources);
            Assert.All(item.Guidance.References,r=>Assert.True(Troubleshooting.IsAllowedReference(r.Url),r.Url));
        }
    }
    [Fact]public void AuditEventLinksUseBothProviderAndNumber()
    {
        Assert.Contains(Event(4625).Guidance.References,r=>r.Url=="https://system32.eventsentry.com/security/event/4625");
        Assert.Contains(Event(4625).Guidance.References,r=>r.Url.EndsWith("event.aspx?eventID=4625"));
        Assert.DoesNotContain(Event(4625,"Other").Guidance.References,r=>r.Url.Contains("4625"));
        Assert.DoesNotContain(Event(1102).Guidance.References,r=>r.Url.Contains("1102"));
        Assert.Contains(Event(1102,"Microsoft-Windows-Eventlog").Guidance.References,r=>r.Url.EndsWith("/1102"));
    }
    [Fact]public void PasswordFailureGuideUsesSubstatusAndOffersConditionalRepair()
    {
        var item=Event(4625,data:CoreTests.Data("Status","0xC000006D")+CoreTests.Data("SubStatus","0xC000006A"));var guide=item.Guidance;
        Assert.True(guide.HasSpecificGuide);Assert.Contains(guide.Checks,c=>c.Contains("outdated credential"));Assert.Contains(guide.Resolutions,c=>c.StartsWith("If a stale credential is confirmed"));
        Assert.Contains("NOT CONFIRMED",guide.Text);Assert.Contains("No repairs are run",guide.Text);
    }
    [Fact]public void KerberosRevocationIsNotPresentedAsCertainLockout()
    {
        var guide=Event(4771,data:CoreTests.Data("Status","0x12")).Guidance;Assert.Contains(guide.Checks,c=>c.Contains("more than a lockout"));
    }
    [Fact]public void SuccessfulAuthenticationDoesNotReceiveFailureFixes()
    {
        var guide=Event(4768,data:CoreTests.Data("Status","0x0")).Guidance;Assert.Empty(guide.PossibleCauses);Assert.DoesNotContain("outdated credential",guide.Text);Assert.Contains("No corrective change",guide.Text);
    }
    [Theory][InlineData(41,"Microsoft-Windows-Kernel-Power","does not establish a failed power supply")][InlineData(1000,"Application Error","not proof that the named DLL is defective")][InlineData(4740,Interpreter.SecurityProvider,"does not prove which application")]
    public void GuidesAvoidInferringCauseFromEventNumber(int id,string provider,string expected)=>Assert.Contains(expected,Event(id,provider).Guidance.Confirmed);
    [Fact]public void TaskActionResultDoesNotImplyBusinessSuccessOrUniversalWindowsError()
    {
        var success=Event(201,"Microsoft-Windows-TaskScheduler",CoreTests.Data("ResultCode","0")).Guidance;Assert.Empty(success.PossibleCauses);Assert.Contains("does not prove",success.Confirmed);
        var failed=Event(201,"Microsoft-Windows-TaskScheduler",CoreTests.Data("ResultCode","5")).Guidance;Assert.Contains("defined by that application",failed.Confirmed);
    }
    [Fact]public void MissingSpecificGuideIsExplicit()
    {
        var guide=Event(999,"Unknown").Guidance;Assert.False(guide.HasSpecificGuide);Assert.Contains("No event-specific resolution",guide.Confirmed);Assert.All(guide.References,r=>Assert.Contains("no",r.Scope,StringComparison.OrdinalIgnoreCase));
    }
    [Theory][InlineData("https://learn.microsoft.com/en-us/troubleshoot/",true)][InlineData("https://system32.eventsentry.com/codes/lookup?query=0xC000006A",true)][InlineData("file:///C:/Windows/System32/cmd.exe",false)][InlineData("https://learn.microsoft.com.attacker.test/",false)][InlineData("https://user:password@learn.microsoft.com/",false)][InlineData("http://system32.eventsentry.com/",false)][InlineData("https://learn.microsoft.com:8443/",false)]
    public void ReferenceLaunchAllowsOnlySupportedHttpsSites(string url,bool expected)=>Assert.Equal(expected,Troubleshooting.IsAllowedReference(url));
    [Fact]public void LinksContainOnlyReferenceIdentifiersAndNeverEventIdentityOrAddresses()
    {
        var item=Event(4625,data:CoreTests.Data("TargetUserName","SensitiveName_xyz")+CoreTests.Data("IpAddress","192.0.2.231")+CoreTests.Data("Status","0xC000006A"));
        var urls=string.Join("\n",item.Guidance.References.Select(r=>r.Url));Assert.DoesNotContain("SensitiveName_xyz",urls);Assert.DoesNotContain("192.0.2.231",urls);Assert.DoesNotContain("DC01",urls);
        Assert.Contains("lookup?query=0xC000006A",urls);
    }
    [Fact]public async Task CsvAndJsonKeepGuidanceAndSources()
    {
        var item=Event(4740);string csv=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".csv"),json=Path.ChangeExtension(csv,"json");
        try
        {
            await Exporter.WriteAsync(csv,[item],false);Assert.Contains("Trace an account lockout",await File.ReadAllTextAsync(csv));
            await Exporter.WriteAsync(json,[item],true);using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(json));var g=doc.RootElement.GetProperty("Events")[0].GetProperty("Guidance");
            Assert.True(g.GetProperty("HasSpecificGuide").GetBoolean());Assert.Contains("Ultimate Windows Security",g.GetProperty("Report").GetString());
        }
        finally{File.Delete(csv);File.Delete(json);}
    }
}
