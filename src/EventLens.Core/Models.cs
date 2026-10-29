using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace EventLens.Core;

public enum EventCategory { Other, SuccessfulAuthentication, FailedAuthentication, Lockout, PasswordChange, AccountChange, GroupChange, Service, ApplicationCrash, Shutdown, ScheduledTask, PolicyChange }
public sealed record IdentityRef(string Name, string Domain, string Sid, string Role)
{
    public string Display => string.IsNullOrWhiteSpace(Domain) ? Name : $"{Domain}\\{Name}";
    public string StableKey(string computer) => UsefulSid(Sid) ? Sid.ToUpperInvariant() : $"{(Domain.Length>0?Domain:computer)}\\{Name}".ToUpperInvariant();
    public static bool UsefulSid(string sid) => sid.StartsWith("S-1-",StringComparison.OrdinalIgnoreCase) && sid is not "S-1-0-0";
}
public sealed class EventItem
{
    public DateTimeOffset? Timestamp { get; init; }
    public int Id { get; init; }
    public int Version { get; init; }
    public string ProviderGuid {get;init;}="";
    public string ActivityId {get;init;}="";
    public string ProcessId {get;init;}="";
    public string ThreadId {get;init;}="";
    public long RecordId { get; init; }
    public string Provider { get; init; }="";
    public string Computer { get; init; }="";
    public string Channel { get; init; }="";
    public string Source { get; init; }="";
    public List<string> ContributingSources {get;}=[];
    public int Level { get; init; }
    public string Keywords { get; init; }="";
    public string OriginalMessage { get; init; }="";
    public string Xml { get; init; }="";
    public Dictionary<string,string> Data { get; init; }=new(StringComparer.OrdinalIgnoreCase);
    public List<IdentityRef> Identities { get; init; }=[];
    public string Title { get; set; }="";
    public string Explanation { get; set; }="";
    public string Failure { get; set; }="";
    public List<DecodedCode> Codes { get; }=[];
    public EventGuidance Guidance=>Troubleshooting.For(this);
    public string CodeDetails=>Codes.Count==0?"No supported numeric code fields are present. Original payload is preserved.":string.Join("\n\n",Codes.Select(c=>$"{c.Field} = {c.Raw}\n{c.Family} · {c.Symbol}\n{c.Description}\n{c.Context}"+(c.Source.Length>0?$"\nReference: {c.Source}":"")));
    public EventCategory Category { get; set; }
    public bool Known { get; set; }
    public string Rule { get; set; }="Original provider message; no matching interpretation rule.";
    public string Relation { get; set; }="";
    public bool IsLive { get; set; }
    public long EstimatedBytes=>(long)Xml.Length*4+OriginalMessage.Length*2L+Explanation.Length*2L+Codes.Sum(c=>(long)(c.Description.Length+c.Context.Length+c.Source.Length)*2+256)+Data.Sum(p=>(long)p.Key.Length*2+p.Value.Length*2)+4096;
    public string Severity => Level switch {1=>"Critical",2=>"Error",3=>"Warning",4=>"Information",5=>"Verbose",_=>Category==EventCategory.FailedAuthentication?"Audit failure":Category==EventCategory.SuccessfulAuthentication?"Audit success":"Audit / other"};
    public string TimestampDisplay => Timestamp?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "Time unavailable";
    public string TimestampUtc => Timestamp?.UtcDateTime.ToString("O") ?? "";
    public string User => Identities.FirstOrDefault(x=>x.Role=="Target")?.Display ?? Identities.FirstOrDefault()?.Display ?? "—";
    public string Workstation=>Field("CallerComputerName","WorkstationName","Workstation");
    public string Address
    {
        get{var value=Field("IpAddress","ClientAddress","SourceNetworkAddress");return System.Net.IPAddress.TryParse(value,out var ip)&&ip.IsIPv4MappedToIPv6?ip.MapToIPv4().ToString():value;}
    }
    public string Origin => Workstation.Length>0?(Address.Length>0?$"{Workstation} ({Address})":Workstation):Address;
    public string Protocol => Field("AuthenticationPackageName","PackageName","LogonProcessName") is {Length:>0} value ? value : Id is 4768 or 4769 or 4771 && Known ? "Kerberos" : Id==4776 && Known ? "NTLM" : "";
    string? cachedKey;
    public string Key => cachedKey ??= $"{(Computer.Length>0?Computer.ToUpperInvariant():Source)}\u001f{Channel.ToUpperInvariant()}\u001f{Provider.ToUpperInvariant()}\u001f{RecordId}\u001f{TimestampUtc}\u001f{Id}\u001f{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Xml)))}";
    public string Field(params string[] names) => names.Select(n=>Data.GetValueOrDefault(n,"")).FirstOrDefault(v=>!string.IsNullOrWhiteSpace(v)&&v!="-") ?? "";
}

public static class EventParser
{
    public static EventItem Parse(string xml,string message="",string source="")
    {
        if (xml.Length>2_000_000) throw new FormatException("Event XML exceeds the 2 MB parsing limit.");
        using var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit, XmlResolver=null,MaxCharactersInDocument=2_000_000 });
        var doc=XDocument.Load(reader);var root=doc.Root ?? throw new FormatException("Missing event root.");
        if(root.Name.LocalName!="Event")throw new FormatException("Expected Event XML.");
        var system=root.Elements().FirstOrDefault(e=>e.Name.LocalName=="System") ?? throw new FormatException("Missing System element.");
        string Sys(string name)=>system.Elements().FirstOrDefault(e=>e.Name.LocalName==name)?.Value ?? "";
        var data=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        int unnamed=0;
        foreach(var container in root.Elements().Where(e=>e.Name.LocalName is "EventData" or "UserData"))
            foreach(var leaf in container.Descendants().Where(e=>!e.HasElements))
            {
                string key=leaf.Attribute("Name")?.Value ?? (leaf.Name.LocalName=="Data"?$"Property[{unnamed++}]":leaf.Name.LocalName);
                if(data.ContainsKey(key)) { int suffix=2;while(data.ContainsKey(key+$"[{suffix}]"))suffix++;key+=$"[{suffix}]"; }
                data[key]=leaf.Value;
            }
        var identities=new List<IdentityRef>();
        string Get(string key)=>data.GetValueOrDefault(key,"");
        void Add(string name,string domain,string sid,string role)
        {
            if(name is "-" or "(null)")name="";
            if(!IdentityRef.UsefulSid(sid))sid="";
            if(name.Length==0&&sid.Length==0)return;
            if(name.Contains('\\')) {var p=name.Split('\\',2);domain=p[0];name=p[1];}
            else if(name.Contains('@')) {var p=name.Split('@',2);name=p[0];domain=p[1];}
            identities.Add(new(name,domain=="-"?"":domain,sid,role));
        }
        foreach(var prefix in new[]{"Target","Subject"})Add(Get(prefix+"UserName"),Get(prefix+"DomainName"),Get(prefix+"UserSid").Length>0?Get(prefix+"UserSid"):Get(prefix+"Sid"),prefix);
        Add(Get("MemberName"),Get("MemberDomainName"),Get("MemberSid"),"Member");
        foreach(var pair in new[]{("AccountName","AccountDomain","AccountSid"),("UserName","DomainName","UserSid"),("User","Domain","Sid")})Add(Get(pair.Item1),Get(pair.Item2),Get(pair.Item3),"Account");
        string systemSid=system.Elements().FirstOrDefault(e=>e.Name.LocalName=="Security")?.Attribute("UserID")?.Value ?? "";
        if(identities.Count==0)Add("","",systemSid,"Recorded principal");
        string time=system.Elements().FirstOrDefault(e=>e.Name.LocalName=="TimeCreated")?.Attribute("SystemTime")?.Value ?? "";
        var item=new EventItem {Id=int.TryParse(Sys("EventID"),out var id)?id:0,RecordId=long.TryParse(Sys("EventRecordID"),out var rid)?rid:0,
            Version=int.TryParse(Sys("Version"),out var version)?version:0,ProviderGuid=system.Elements().FirstOrDefault(e=>e.Name.LocalName=="Provider")?.Attribute("Guid")?.Value??"",
            ActivityId=system.Elements().FirstOrDefault(e=>e.Name.LocalName=="Correlation")?.Attribute("ActivityID")?.Value??"",ProcessId=system.Elements().FirstOrDefault(e=>e.Name.LocalName=="Execution")?.Attribute("ProcessID")?.Value??"",ThreadId=system.Elements().FirstOrDefault(e=>e.Name.LocalName=="Execution")?.Attribute("ThreadID")?.Value??"",
            Provider=system.Elements().FirstOrDefault(e=>e.Name.LocalName=="Provider")?.Attribute("Name")?.Value ?? "",Computer=Sys("Computer"),Channel=Sys("Channel"),Level=int.TryParse(Sys("Level"),out var level)?level:0,
            Timestamp=DateTimeOffset.TryParse(time,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var ts)?ts:null,
            Keywords=Sys("Keywords"),Data=data,Identities=identities,OriginalMessage=message,Xml=xml,Source=source};
        Interpreter.Apply(item);return item;
    }
}

public sealed record LogSource(string Computer,string Path,bool IsFile=false)
{
    public string Key=>$"{Computer}|{Path}|{IsFile}";
    public string Display=>IsFile?System.IO.Path.GetFileName(Path):$"{Computer} / {Path}";
}
public sealed record QuerySpec
{
    public EventSelector[] Selectors {get;init;}=[];
    public DateTimeOffset? From { get; init; }=DateTimeOffset.UtcNow.AddHours(-24);
    public DateTimeOffset? To { get; init; }
    public int[] EventIds { get; init; }=[];
    public string Provider { get; init; }="";
    public int? MaximumLevel { get; init; }
    public string Identity { get; init; }="";
    public string Text { get; init; }="";
    public string Computer { get; init; }="";
    public string Origin { get; init; }="";
    public string Protocol { get; init; }="";
    public string Failure { get; init; }="";
    public EventCategory? Category { get; init; }
    public string Mode=>"Native time / event ID / provider / severity filters; identity and text use a bounded payload/message scan (not an index).";
}
public sealed record EventSelector(string Provider,int[] Ids);
public sealed record SourceIssue(string Source,string Message);
public sealed record SearchProgress(long Scanned,long Matches,bool Completed,IReadOnlyList<SourceIssue> Issues);
public sealed record EventPage(IReadOnlyList<EventItem> Events,bool HasMore,long Scanned,IReadOnlyList<SourceIssue> Issues);
public sealed record SavedSearch(string Name,QuerySpec Filter,string TimeRange="Last 24 hours",string[]? Computers=null,string[]? Channels=null);
public sealed record AppSettings
{
    public List<SavedSearch> Searches { get; init; }=[];
    public List<string> Computers { get; init; }=["localhost"];
    public List<string> HiddenColumns { get; init; }=["Origin","Protocol","Channel"];
    public Dictionary<string,double> ColumnWidths { get; init; }=[];
}
