using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace EventLens.Core;

public sealed record ReferenceEntry(string Symbol,string Description);
public sealed record ReferenceSource(string Url,string Sha256,int Entries);
public sealed record CatalogData(string RetrievedUtc,Dictionary<string,ReferenceSource> Sources,Dictionary<string,Dictionary<string,List<ReferenceEntry>>> Codes,Dictionary<string,string> Security);
public sealed record DecodedCode(string Field,string Raw,string Family,string Symbol,string Description,string Context,string Source);

/// <summary>Offline Microsoft reference text. Numeric values are never interpreted without a code family.</summary>
public static class CodeCatalog
{
    static readonly Lazy<CatalogData> Data=new(()=>
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("EventLens.Core.Reference.catalog.json") ?? throw new InvalidOperationException("Missing embedded code catalog.");
        return JsonSerializer.Deserialize<CatalogData>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? throw new InvalidOperationException("Invalid embedded code catalog.");
    });
    public static IReadOnlyDictionary<string,ReferenceSource> Sources=>Data.Value.Sources;
    public static int SecurityCount=>Data.Value.Security.Count;
    public static string Summary=>$"Offline Microsoft references: {string.Join(" · ",Sources.Where(p=>p.Key!="security").Select(p=>$"{p.Value.Entries:N0} {p.Key}"))} · {SecurityCount:N0} Security audit event labels. Retrieved {Data.Value.RetrievedUtc[..10]}.";
    public static string Coverage=>Summary+"\n\nThis is the published MS-ERREF table set and the documented Kerberos / audit-event lists, not every code or provider Microsoft has ever shipped. Event IDs are provider-specific; field names alone do not always identify a code family. A reference label is not a root-cause diagnosis. Curated investigation rules cover a smaller subset. Unknown values remain visible, with their original payload and provider text. The app does not download references or send events anywhere.\n\n"+string.Join("\n\n",Sources.Select(p=>$"{p.Key}: {p.Value.Entries:N0} entries\n{p.Value.Url}\nSource HTML SHA-256: {p.Value.Sha256}"));
    public static string? SecurityTitle(int id)=>Data.Value.Security.GetValueOrDefault(id.ToString(CultureInfo.InvariantCulture));
    public static DecodedCode? Lookup(string family,uint code,string field="",string raw="",string context="Documented code-family lookup")
    {
        if(Data.Value.Codes.TryGetValue(family,out var entries)&&entries.TryGetValue(code.ToString(CultureInfo.InvariantCulture),out var matches))
            return new(field,raw,family,string.Join(" / ",matches.Select(m=>m.Symbol)),string.Join("\n",matches.Select(m=>m.Description).Distinct()),context,Sources[family].Url);
        // HRESULT_FROM_WIN32 encodes the Win32 code in the low 16 bits (facility 7).
        if(family=="hresult" && (code&0xFFFF0000)==0x80070000 && Lookup("win32",code&0xFFFF) is {} win32)
            return win32 with{Field=field,Raw=raw,Family="hresult → win32",Symbol="HRESULT_FROM_WIN32("+win32.Symbol+")",Context=context};
        if(family=="hresult" && (code&0x10000000)!=0 && Lookup("ntstatus",code&~0x10000000u) is {} nt)
            return nt with{Field=field,Raw=raw,Family="hresult → ntstatus",Symbol="HRESULT_FROM_NT("+nt.Symbol+")",Context=context};
        return null;
    }
    static readonly Dictionary<uint,string> LogonTypes=new()
    {
        [2]="Interactive: signed in locally",[3]="Network: accessed this computer over the network",[4]="Batch: a batch process, commonly a scheduled task",[5]="Service: a service started a logon session",[7]="Unlock: an existing workstation session was unlocked",[8]="NetworkCleartext: credentials were supplied to the authentication package in clear text; this does not alone establish whether transport encryption was used",[9]="NewCredentials: cloned local identity with different credentials for outbound connections",[10]="RemoteInteractive: remote desktop or terminal services",[11]="CachedInteractive: locally cached domain credentials; the domain controller was not contacted to verify them",[12]="CachedRemoteInteractive: cached remote interactive logon (internal auditing)",[13]="CachedUnlock: cached workstation unlock"
    };
    public static void Enrich(EventItem item)
    {
        bool security=item.Provider.Equals(Interpreter.SecurityProvider,StringComparison.OrdinalIgnoreCase)&&!SchemaSupport.Unreviewed(item);
        if(security && !item.Known && SecurityTitle(item.Id) is {} title)
        {
            item.Known=true;item.Title=title;item.Rule="Provider-scoped Microsoft audit reference label; no detailed investigation rule";
            item.Explanation="Windows recorded: "+title+"\n\n"+(item.OriginalMessage.Length>0?item.OriginalMessage:"The documented label identifies the activity. Provider-rendered text is unavailable; inspect the payload for the recorded details. This label alone does not establish cause or intent.");
        }
        item.Codes.Clear();
        foreach(var (field,raw) in item.Data)
        {
            if(raw.Length>256)continue;
            if(security && field.Equals("LogonType",StringComparison.OrdinalIgnoreCase))
            {
                var n=Interpreter.Code(raw);item.Codes.Add(new(field,raw,"logon type","",n is {} v&&LogonTypes.TryGetValue(v,out var text)?text:"Unmapped logon type; raw value preserved","Security logon schema","https://learn.microsoft.com/en-us/windows/security/threat-protection/auditing/event-4624"));continue;
            }
            string? family=null;
            bool authStatus=field.Equals("Status",StringComparison.OrdinalIgnoreCase)||field.Equals("SubStatus",StringComparison.OrdinalIgnoreCase)||field.Equals("FailureCode",StringComparison.OrdinalIgnoreCase)||field.Equals("ErrorCode",StringComparison.OrdinalIgnoreCase);
            if(security && authStatus && item.Id is 4625 or 4776)family="ntstatus";
            else if(security && authStatus && item.Id is 4768 or 4769 or 4770 or 4771 or 4772)family="kerberos";
            else if(field.Equals("HResult",StringComparison.OrdinalIgnoreCase))family="hresult";
            else if(field.Equals("Win32Error",StringComparison.OrdinalIgnoreCase)||field.Equals("Win32ErrorCode",StringComparison.OrdinalIgnoreCase))family="win32";
            else if(item.Provider.Equals("Application Error",StringComparison.OrdinalIgnoreCase)&&item.Id==1000&&field.Equals("ExceptionCode",StringComparison.OrdinalIgnoreCase))family="ntstatus";
            // TaskScheduler action exit codes may be application-defined; do not treat ResultCode as HRESULT.
            bool candidate=family is null && (authStatus||field.Equals("ResultCode",StringComparison.OrdinalIgnoreCase)||field.Equals("ErrorValue",StringComparison.OrdinalIgnoreCase)||field.Equals("ExceptionCode",StringComparison.OrdinalIgnoreCase));
            if(family is null&&!candidate)continue;
            var code=Interpreter.Code(raw);
            // Application Error's ExceptionCode is HexInt32 and is commonly rendered without 0x.
            if(family=="ntstatus" && item.Provider.Equals("Application Error",StringComparison.OrdinalIgnoreCase)&&item.Id==1000&&field.Equals("ExceptionCode",StringComparison.OrdinalIgnoreCase)&&raw.Length is >0 and <=8&&uint.TryParse(raw,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var exception))code=exception;
            if(code is null){item.Codes.Add(new(field,raw,family??"unspecified","","Not a recognized 32-bit numeric code; raw value preserved","No numeric interpretation",""));continue;}
            if(family is not null)
            {
                var match=Lookup(family,code.Value,field,raw,"Code family identified by provider/event schema or explicit field name");
                if(item.Id==4625 && field.Equals("SubStatus",StringComparison.OrdinalIgnoreCase)&&code==0)
                    match=new(field,raw,"ntstatus","STATUS_SUCCESS (zero substatus)","No additional failure substatus was supplied. The failed sign-in event and its main Status still determine the outcome.","4625 substatus context",Sources["ntstatus"].Url);
                item.Codes.Add(match??new(field,raw,family,"",$"Unmapped {family} code 0x{code:X8}; no explanation invented","Identified code family; value absent from bundled reference",Sources[family].Url));
            }
            else
            {
                item.Codes.Add(new(field,raw,"unspecified","","The code family is not established. The following reference matches are possibilities, not a diagnosis; the provider or application may define a different meaning.","Ambiguous field: consult the provider schema",""));
                foreach(string possible in new[]{"win32","hresult","ntstatus"})
                    if(Lookup(possible,code.Value,field,raw,"Candidate only — code family not established") is {} match)item.Codes.Add(match);
            }
        }
    }
}
