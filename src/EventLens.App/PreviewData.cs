using EventLens.App.ViewModels;
using EventLens.Core;
namespace EventLens.App;

// Synthetic fixtures are only selected through the explicit developer --preview mode.
// The normal application always queries actual Windows sources.
public static class PreviewData
{
    public static void Apply(MainViewModel vm)
    {
        vm.Section="User Investigation";vm.Identity="CONTOSO\\jsmith";vm.Channels=["Security"];vm.Computers=["DC01.contoso.test","DC02.contoso.test"];vm.SourcesChanged();
        var start=new DateTimeOffset(2026,10,22,14,30,0,TimeSpan.Zero);
        for(int index=0;index<28;index++)
        {
            int id=index==0?4624:index==1?4767:index==2?4740:index%3==0?4771:index%3==1?4776:4625;
            string code=id==4771?"0x18":id is 4625 or 4776?"0xC000006A":"0x0";
            string xml=$"<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='{Interpreter.SecurityProvider}'/><EventID>{id}</EventID><Level>0</Level><TimeCreated SystemTime='{start.AddMinutes(-index):O}'/><EventRecordID>{1000-index}</EventRecordID><Computer>{(index%2==0?"DC01":"DC02")}.contoso.test</Computer><Channel>Security</Channel></System><EventData><Data Name='TargetUserName'>jsmith</Data><Data Name='TargetDomainName'>CONTOSO</Data><Data Name='TargetUserSid'>S-1-5-21-100-200-300-1001</Data><Data Name='Status'>{code}</Data><Data Name='SubStatus'>{code}</Data><Data Name='WorkstationName'>WS-024</Data><Data Name='CallerComputerName'>WS-024</Data><Data Name='IpAddress'>192.0.2.24</Data><Data Name='AuthenticationPackageName'>{(id==4771?"Kerberos":"NTLM")}</Data></EventData></Event>";
            if(index==6)xml="<Event><System><Provider Name='Microsoft-Windows-Time-Service'/><EventID>134</EventID><Level>3</Level><TimeCreated SystemTime='"+start.AddMinutes(-index).ToString("O")+"'/><EventRecordID>994</EventRecordID><Computer>DC01.contoso.test</Computer><Channel>System</Channel></System><EventData/></Event>";
            var e=EventParser.Parse(xml,index==6?"Time service could not reach its configured time source. Synthetic warning fixture.":"", "Synthetic preview");e.IsLive=index<3;vm.Events.Add(e);
        }
        vm.Selected=vm.Events[4];vm.Status="SYNTHETIC PREVIEW · 28 fixture records · no real account or machine data";
        vm.Notice="Preview only. Normal launch reads actual Windows Event Logs. Counts and correlations always cover loaded results only.";
        vm.RefreshForPreview();
    }
}
