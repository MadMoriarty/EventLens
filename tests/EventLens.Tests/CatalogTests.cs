using EventLens.Core;
using Xunit;
namespace EventLens.Tests;

public class CatalogTests
{
    [Fact]public void PublishedSnapshotHasAuditableCoverage()
    {
        Assert.Equal(1799,CodeCatalog.Sources["ntstatus"].Entries);Assert.Equal(2704,CodeCatalog.Sources["win32"].Entries);
        Assert.Equal(2928,CodeCatalog.Sources["hresult"].Entries);Assert.Equal(65,CodeCatalog.Sources["kerberos"].Entries);Assert.Equal(342,CodeCatalog.SecurityCount);
        Assert.All(CodeCatalog.Sources.Values,s=>{Assert.Equal(64,s.Sha256.Length);Assert.StartsWith("https://learn.microsoft.com/",s.Url);});
        Assert.Contains("not every code",CodeCatalog.Coverage);
    }
    [Theory][InlineData("ntstatus",0xC000006Au,"STATUS_WRONG_PASSWORD")][InlineData("win32",5u,"ERROR_ACCESS_DENIED")][InlineData("hresult",0x80004005u,"E_FAIL")][InlineData("kerberos",24u,"KDC_ERR_PREAUTH_FAILED")]
    public void OfficialFamiliesRetainExactSymbols(string family,uint code,string expected)
    {
        var match=CodeCatalog.Lookup(family,code);Assert.NotNull(match);Assert.Contains(expected,match.Symbol);Assert.NotEmpty(match.Description);
    }
    [Fact]public void SameNumericValueDoesNotImplySameMeaning()
    {
        Assert.Contains("ERROR_INVALID_DATA",CodeCatalog.Lookup("win32",13)!.Symbol);Assert.Contains("KDC_ERR_BADOPTION",CodeCatalog.Lookup("kerberos",13)!.Symbol);
    }
    [Theory][InlineData("c0000005", "STATUS_ACCESS_VIOLATION")][InlineData("80000003","STATUS_BREAKPOINT")]
    public void ApplicationErrorExceptionCodeUsesItsDocumentedHexEncoding(string raw,string symbol)
    {
        var item=EventParser.Parse(CoreTests.Xml(1000,"Application Error",CoreTests.Data("ExceptionCode",raw)));Assert.Equal(symbol,Assert.Single(item.Codes).Symbol);
    }
    [Theory][InlineData("-2147024891",0x80070005u)][InlineData("0xC000006A",0xC000006Au)][InlineData("3221225578",0xC000006Au)]
    public void SignedAndUnsignedCodesUseSameBits(string raw,uint expected)=>Assert.Equal(expected,Interpreter.Code(raw));
    [Fact]public void DerivedHresultUnwrapsOnlyDocumentedFacilities()
    {
        Assert.Equal("E_ACCESSDENIED",CodeCatalog.Lookup("hresult",0x80070005)!.Symbol);
        Assert.Contains("HRESULT_FROM_WIN32",CodeCatalog.Lookup("hresult",0x80070035)!.Symbol);
        Assert.Contains("STATUS_WRONG_PASSWORD",CodeCatalog.Lookup("hresult",0xD000006A)!.Symbol);
        Assert.Null(CodeCatalog.Lookup("hresult",0xF1234567));
    }
    [Fact]public void AmbiguousApplicationExitCodeIsNotClaimedAsWindowsFailure()
    {
        var item=EventParser.Parse(CoreTests.Xml(201,"Microsoft-Windows-TaskScheduler",CoreTests.Data("ResultCode","5")));
        Assert.Equal("unspecified",item.Codes[0].Family);Assert.Contains("not a diagnosis",item.Codes[0].Description);Assert.All(item.Codes.Skip(1),c=>Assert.Contains("Candidate only",c.Context));
    }
    [Fact]public void SecurityStatusSubstatusAndLogonTypeAreExplainedSeparately()
    {
        var item=EventParser.Parse(CoreTests.Xml(4625,data:CoreTests.Data("Status","0xC000006D")+CoreTests.Data("SubStatus","0x0")+CoreTests.Data("LogonType","10")));
        Assert.Contains(item.Codes,c=>c.Field=="SubStatus"&&c.Description.StartsWith("No additional"));
        Assert.Contains(item.Codes,c=>c.Family=="logon type"&&c.Description.Contains("remote desktop"));
    }
    [Fact]public void UnmappedProtocolStatusNeverReceivesOtherFamilyMeaning()
    {
        var item=EventParser.Parse(CoreTests.Xml(4771,data:CoreTests.Data("Status","0xDEADBEEF")));
        var decoded=Assert.Single(item.Codes);Assert.Equal("kerberos",decoded.Family);Assert.Contains("Unmapped",decoded.Description);
    }
    [Fact]public void ReferenceAuditLabelsStayProviderScoped()
    {
        var known=EventParser.Parse(CoreTests.Xml(4688));Assert.True(known.Known);Assert.Contains("process",known.Title,StringComparison.OrdinalIgnoreCase);Assert.Contains("no detailed investigation rule",known.Rule);
        var other=EventParser.Parse(CoreTests.Xml(4688,"OtherProvider"));Assert.False(other.Known);
        Assert.False(EventParser.Parse(CoreTests.Xml(1102)).Known);Assert.True(EventParser.Parse(CoreTests.Xml(1102,"Microsoft-Windows-Eventlog")).Known);
    }
    [Fact]public async Task ExportsPreserveExplanationsAndDecodedReferences()
    {
        var item=EventParser.Parse(CoreTests.Xml(4625,data:CoreTests.Data("Status","0xC000006A")));string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".csv");
        try{await Exporter.WriteAsync(path,[item],false);var csv=await File.ReadAllTextAsync(path);Assert.Contains("CodeDetails,InterpretationRule",csv);Assert.Contains("STATUS_WRONG_PASSWORD",csv);Assert.Contains("https://learn.microsoft.com/",csv);}finally{File.Delete(path);}
    }
}
