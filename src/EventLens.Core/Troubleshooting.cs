using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace EventLens.Core;

public sealed record KnowledgeLink(string Source,string Title,string Url,string Scope,string ReviewedOn="2026-10-22");
public sealed record KnowledgeIndexSource(string Url,string Sha256,Dictionary<string,string> Events);
public sealed record KnowledgeIndex(string ReviewedOn,Dictionary<string,KnowledgeIndexSource> Sources);
public sealed record EventGuidance(string Title,string Confirmed,string[] PossibleCauses,string[] Checks,string[] Resolutions,string AppliesTo,string[] RelatedEvents,IReadOnlyList<KnowledgeLink> References,bool HasSpecificGuide)
{
    public string Text=>Title+"\n\nWHAT THE EVENT ESTABLISHES\n"+Confirmed+Section("POSSIBLE CAUSES — NOT CONFIRMED",PossibleCauses)+Section("WHAT TO CHECK NEXT",Checks)+Section("RESOLUTION — AFTER CONFIRMING THE CAUSE",Resolutions)+Section("RELATED EVIDENCE",RelatedEvents)+"\n\nAPPLIES TO\n"+AppliesTo+"\n\nGUIDANCE\nEventLens-authored investigation steps; reviewed 2026-10-22. Reference links identify the supporting documentation. A matching event does not prove a cause. No repairs are run by this tool.";
    public string Report=>Text+"\n\nREFERENCES\n"+string.Join("\n\n",References.Select(r=>$"{r.Source}: {r.Title}\n{r.Scope}\n{r.Url}\nReviewed {r.ReviewedOn}"));
    static string Section(string heading,IEnumerable<string> entries)=>entries.Any()?"\n\n"+heading+"\n"+string.Join("\n",entries.Select((s,i)=>$"{i+1}. {s}")):"";
}

public static class Troubleshooting
{
    const string MsRoot="https://learn.microsoft.com/en-us/troubleshoot/";
    const string AuditRoot="https://learn.microsoft.com/en-us/windows/security/threat-protection/auditing/event-";
    const string Crash="https://learn.microsoft.com/en-us/troubleshoot/windows-server/performance/troubleshoot-application-service-crashing-behavior";
    const string Restart="https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/event-id-41-restart";
    const string Tasks="https://learn.microsoft.com/en-us/troubleshoot/windows-server/system-management-components/troubleshoot-scheduled-tasks-not-running";
    const string Lockout="https://learn.microsoft.com/en-us/troubleshoot/windows-server/windows-security/account-lockout-and-management-tool";
    static readonly Lazy<KnowledgeIndex> Index=new(()=>
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("EventLens.Core.Reference.knowledge-links.json")??throw new InvalidOperationException("Missing knowledge links.");
        return JsonSerializer.Deserialize<KnowledgeIndex>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Invalid knowledge links.");
    });
    public static IReadOnlyList<KnowledgeLink> Sources {get;}=[
        new("Microsoft","Microsoft troubleshooting library",MsRoot,"Official diagnostic and resolution articles; selected relevant articles are linked on events."),
        new("Ultimate Windows Security","Windows Security Log Encyclopedia","https://www.ultimatewindowssecurity.com/securitylog/encyclopedia/default.aspx","Independent event and field reference. Online articles are linked, not bundled."),
        new("EventSentry","Security Log Defined and Windows code lookup","https://system32.eventsentry.com/","Independent audit/event relationships and code references. Online articles are linked, not bundled.")];
    public static string Coverage=>$"All three reference sources are integrated. Offline EventLens guidance is available for authentication failures, lockouts, successful logons, account/password/group changes, process creation, audit-log clearing, service failures/installations, application crashes, unexpected shutdowns and scheduled-task outcomes.\n\nIndexed event-page links: {Index.Value.Sources["ultimate"].Events.Count} Ultimate Windows Security; {Index.Value.Sources["eventsentry"].Events.Count} EventSentry. These are provider-scoped links, not counts of offline articles or guaranteed resolutions. Other events receive an explicit general investigation path.\n\nThird-party articles remain online; complete website databases are not embedded. Offline numeric decoding and reviewed EventLens guidance work without the internet. Links open only when clicked, and carry reference IDs, never record contents, account names, hostnames or IPs.\n\nLink indexes reviewed {Index.Value.ReviewedOn}.\n"+string.Join("\n",Index.Value.Sources.Select(p=>$"{p.Key} index SHA-256: {p.Value.Sha256}"));
    public static bool IsAllowedReference(string value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.IsDefaultPort&&uri.UserInfo.Length==0&&uri.Host is "learn.microsoft.com" or "www.ultimatewindowssecurity.com" or "system32.eventsentry.com";
    public static EventGuidance For(EventItem e)
    {
        if(SchemaSupport.Unreviewed(e))return new("Unreviewed event schema",SchemaSupport.Notice(e),[],["Compare the original provider message, event version and raw payload with documentation for the source Windows version."],["No code-specific repair is established by this unreviewed schema."],"Version-sensitive authentication event; detailed guidance withheld.",[],Sources,false);
        string title="General investigation",confirmed=$"Provider {e.Provider}, event {e.Id}, recorded severity {e.Severity}. Read the original message and payload for the recorded activity. No event-specific resolution guide is available.";
        string[] causes=[],checks=["Match the provider and event ID together. Inspect the original message, decoded code family and payload.","Compare nearby events on the same computer and confirm the Windows/application version before following an article."],fixes=["Identify and confirm the failing component before making changes. General reference matches do not establish a repair."],related=[];
        string applies="General Windows event investigation; product-specific instructions require a matching product and version.";
        bool specific=false,security=e.Provider.Equals(Interpreter.SecurityProvider,StringComparison.OrdinalIgnoreCase);
        var links=new List<KnowledgeLink>();
        void Ms(string name,string url,string scope)=>links.Add(new("Microsoft",name,url,scope));
        void Guide(string heading,string fact,string[] possible,string[] next,string[] resolution,string scope,string[] evidence)
        {specific=true;title=heading;confirmed=fact;causes=possible;checks=next;fixes=resolution;applies=scope;related=evidence;}
        if(security)
        {
            if(e.Id is 4625 or 4776 or 4768 or 4769 or 4771 or 4772 or 4773)
            {
                bool failure=e.Category==EventCategory.FailedAuthentication||e.Id is 4771 or 4772 or 4773;
                Guide(failure?"Investigate an authentication failure":"Review an authentication outcome",failure?"The provider recorded an unsuccessful authentication operation. The status/substatus describes the recorded rejection; it does not identify intent or the process holding credentials.":"Inspect the recorded protocol status. A ticket request or credential-validation event does not by itself prove an interactive desktop sign-in.",
                    failure?["The recorded rejection may involve credentials, account restrictions, ticket policy or connectivity. Use the protocol-specific code to narrow this list."]:[],
                    ["Read Status/SubStatus under Explain. Kerberos and NTLM use different code families.","Check the affected account, logon type, recorded source, process information and time. Source fields can be absent or represent an intermediary."],
                    failure?["Apply the matching code-specific checks below. Confirm where the request originated before updating credentials or changing access."]:["No corrective change is established by a successful authentication event. Investigate only if the activity is unexpected."],
                    "Windows Security Auditing; local accounts, AD and Kerberos/NTLM differ. Domain-wide investigations may require multiple domain controllers.",
                    ["Security-Auditing 4625: local failed logon; 4776: NTLM validation; 4768/4769/4771: Kerberos requests/failures.","Security-Auditing 4740: lockout; 4767: unlock; 4624: successful logon. Correlation is supporting evidence, not causation."]);
                Ms("Authentication event fields",AuditRoot+(e.Id is 4772 or 4773?4771:e.Id),"Provider-specific event reference; check recorded protocol and fields.");
                if(failure)AddFailureChecks(e,ref causes,ref checks,ref fixes);
            }
            else if(e.Id==4740)
            {
                Guide("Trace an account lockout","An account lockout was recorded. CallerComputerName is a source clue and does not prove which application supplied a password.",
                    ["Repeated manual errors, a stale saved credential or unauthorized attempts may contribute. A lockout event alone does not distinguish them."],
                    ["Correlate preceding 4625/4771/4776 failures across relevant controllers and the reported source.","Check account-using services, scheduled tasks and saved connections on the suspected source. Compare timing with password changes."],
                    ["Correct the confirmed source of failed attempts before unlocking the account under your normal procedure.","If attempts are unexpected, follow your incident process. Do not weaken the lockout policy to hide the symptom."],
                    "Windows/AD account lockouts. Managed Entra Domain Services and local accounts can have different policies and diagnostic access.",
                    ["Security-Auditing 4625, 4771, 4776 before lockout; 4767/4624 after recovery."]);
                Ms("Account lockout event",AuditRoot+"4740","Recorded lockout fields.");Ms("Account lockout investigation tools",Lockout,"AD troubleshooting reference; tool compatibility and prerequisites must be checked.");
            }
            else if(e.Id==4624)
                Guide("Review a successful logon","Windows recorded a successful logon. Logon type determines the kind of session; success alone is not evidence of compromise.",[],["Compare the affected identity, logon type, source and authentication package with expected use.","Use the logon ID and computer to relate subsequent activity; IDs are not globally unique across machines or reboots."],["No repair is indicated by this event alone. Investigate unexpected use through your normal security process."],"Windows Security Auditing logons.",["Security-Auditing 4634/4647: session termination; 4672: special privileges; 4688: process creation, when auditing is enabled."]);
            else if(e.Id is 4723 or 4724)
                Guide("Review a password operation","Windows recorded a password change/reset attempt. Audit success/failure keywords establish the recorded outcome; an attempt is not automatically a success.",[],["Distinguish the initiating Subject from the affected Target. Confirm the audit outcome and whether the operation was authorized."],["After an authorized successful change, update any confirmed dependent credentials through their normal configuration. Do not reset a password based solely on an ambiguous attempt."],"Windows Security Auditing account/password management.",["Security-Auditing 4625/4771/4776 for subsequent failures; 4740 for lockout."]);
            else if(e.Category is EventCategory.AccountChange or EventCategory.GroupChange or EventCategory.PolicyChange)
                Guide("Review an audited configuration change","The event describes an account, group or policy operation. The actor and affected object can be different identities.",[],["Compare Subject, Target/Member and audit outcome with an approved change. Inspect the before/after fields when available."],["If the change is unexpected, confirm its scope and authorization before following your rollback or incident procedure. There is no universal repair for an audit event."],"Windows Security Auditing; the exact event schema determines which change and outcome were recorded.",["Inspect nearby account/group/policy audit events for the same object and actor."]);
            else if(e.Id==4688)
                Guide("Review process creation","Windows recorded creation of a process. This is activity evidence, not a crash or proof of malicious behavior.",[],["Inspect the executable path, initiating identity, creator process and command line if the audit policy included it.","Compare with expected software and the corresponding logon session."],["No repair is indicated by process creation alone. Investigate an unexpected executable or parent process before taking action."],"Windows Security Auditing process creation; command-line inclusion depends on policy.",["Security-Auditing 4624 logon and 4689 process termination, when retained and audited."]);
        }
        if(e.Provider.Equals("Microsoft-Windows-Eventlog",StringComparison.OrdinalIgnoreCase)&&e.Id==1102)
        {
            Guide("Review a cleared Security log","Windows recorded that the Security audit log was cleared. The event alone does not establish whether this was authorized.",[],["Identify the recorded account and compare the time with approved maintenance and retained central logs."],["Follow your evidence-retention and incident procedure for an unauthorized clear. Deleted local history is not recovered by changing an EventLens filter."],"Microsoft-Windows-Eventlog event 1102 in the Security channel.",["Compare centrally forwarded records and nearby log/service events."]);
            Ms("Security audit log clearing event",AuditRoot+"1102","Microsoft-Windows-Eventlog provider, not Security-Auditing.");
        }
        else if((e.Provider.Equals("Microsoft-Windows-Kernel-Power",StringComparison.OrdinalIgnoreCase)&&e.Id==41)||(e.Provider.Equals("EventLog",StringComparison.OrdinalIgnoreCase)&&e.Id==6008))
        {
            Guide("Investigate an unexpected shutdown","Windows recorded an unclean shutdown. Event 41/6008 alone does not establish a failed power supply, driver or other root cause.",
                ["A Stop error, power interruption, forced power-off or an unresponsive system can precede an unclean shutdown."],
                ["Compare the reboot time with event 41, the decimal BugcheckCode, power-button fields and available crash dumps.","A zero bugcheck code does not prove a power fault. Check the preceding System events and observed circumstances."],
                ["Follow the Microsoft scenario matching the recorded evidence. Use a crash dump for Stop errors; check power/hardware conditions when evidence supports that path."],
                "Microsoft guidance targets supported Windows Client versions; verify applicability on Windows Server and specific hardware.",
                ["Kernel-Power 41; EventLog 6008; relevant BugCheck / Windows Error Reporting records."]);
            Ms("Unexpected shutdown diagnostic scenarios",Restart,"Official investigation paths; event 41 contains a decimal bugcheck code.");
        }
        else if((e.Provider.Equals("Windows Error Reporting",StringComparison.OrdinalIgnoreCase)&&e.Id==1001)||(e.Provider.Equals(".NET Runtime",StringComparison.OrdinalIgnoreCase)&&e.Id==1026)||(e.Provider.Equals("Application Hang",StringComparison.OrdinalIgnoreCase)&&e.Id==1002))
        {
            Guide("Correlate application failure evidence","Windows recorded an error report, runtime exception or application hang. The exact provider and payload distinguish these operations; an error report is not itself a confirmed root cause.",[],
                ["Match the application path, process/instance identifiers and time with Application Error 1000 and the application's own diagnostics.","Inspect the recorded report/problem signature or exception text. Process IDs can be reused; an ID alone does not join events across restarts or computers."],
                ["Choose the vendor/runtime diagnostic path supported by the matching evidence. A crash dump or hang dump may be required; do not replace system DLLs based on a report name."],
                "Windows Error Reporting 1001, .NET Runtime 1026 or Application Hang 1002. Application and runtime versions affect troubleshooting.",
                ["Application Error 1000, relevant runtime records and matching application/dump evidence."]);
            Ms("Application failure diagnostic guidance",Crash,"Crash/service diagnostic paths; a hang or report may require a different dump collection procedure.");
        }
        else if(e.Provider.Equals("Application Error",StringComparison.OrdinalIgnoreCase)&&e.Id==1000)
        {
            Guide("Investigate an application crash","Application Error recorded a crash. The faulting module and exception code are clues, not proof that the named DLL is defective.",
                ["The application, a loaded module, a driver interaction or another runtime condition may be involved."],
                ["Record the application/module paths, versions, exception code and time. Check related Windows Error Reporting records.","Check supported application/OS updates and reproduce the issue if appropriate. A crash dump may be needed to identify the failing call path."],
                ["Apply the vendor fix or configuration correction supported by the investigation. Collect a dump using the documented process if the fault remains unexplained."],
                "Application Error event 1000; Microsoft crash guidance covers supported Windows Client and Server. Application-specific fixes depend on version.",
                ["Windows Error Reporting 1001 and matching application diagnostics/crash dumps."]);
            Ms("Application and service crash investigation",Crash,"Crash evidence, faulting module analysis and dump collection guidance.");
        }
        else if(e.Provider.Equals("Microsoft-Windows-TaskScheduler",StringComparison.OrdinalIgnoreCase)&&e.Id is 101 or 103 or 201 or 203)
        {
            var result=Interpreter.Code(e.Field("ResultCode","ErrorValue"));bool failed=e.Id!=201||result is >0;
            Guide("Investigate a scheduled-task outcome",failed?"Task Scheduler recorded a launch/action problem or nonzero result. An action's exit code can be defined by that application.":"Inspect the action result and task history. A zero action exit code alone does not prove the intended business operation completed.",
                failed?["The task action, execution account, working directory, conditions or application-defined exit behavior may be involved."]:[],
                ["Review the task History and Last Run Result alongside the configured action, arguments and execution account.","Compare the action's behavior under equivalent identity and working-directory conditions. Consult the executable's own exit-code documentation."],
                ["Correct only the confirmed action/configuration problem, then verify the intended output and subsequent task history."],
                "TaskScheduler Operational events; the linked troubleshooting article targets supported Windows Server versions.",
                ["Other Microsoft-Windows-TaskScheduler events for the same task instance, plus the action's own logs."]);
            Ms("Scheduled task diagnostic steps",Tasks,"Task status, history, action testing and further diagnostics.");
        }
        else if(e.Provider.Equals("Service Control Manager",StringComparison.OrdinalIgnoreCase)&&e.Id is 7000 or 7001 or 7009 or 7011 or 7023 or 7024 or 7031 or 7034 or 7038 or 7045)
        {
            bool install=e.Id==7045;
            Guide(install?"Review a service installation":"Investigate a service failure",install?"Service Control Manager recorded a service installation. Installation alone is not a failure or proof of malicious activity.":"Service Control Manager recorded a startup, timeout, logon or termination problem. The original message and service-specific error are needed to distinguish the cause.",
                install?[]:["The specific event may involve dependencies, execution-account rights, an inaccessible binary/configuration, a timeout or an application crash."],
                ["Inspect the original message, named service, account, binary path and nearby Application/System events. Do not assume all SCM events share the same cause."],
                install?["Confirm that the installation and service configuration were approved before making any change."]:["Use the service/vendor's matching diagnostic procedure. Correct a verified dependency, account/configuration problem, or investigate a crash when the evidence supports it. Recheck the service outcome."],
                "Service Control Manager on Windows. Service-specific products and errors require matching vendor documentation.",
                ["Relevant Service Control Manager events; Application Error 1000 / Windows Error Reporting 1001 if a crash was also recorded."]);
            if(!install&&e.Id is 7031 or 7034)Ms("Service crash investigation",Crash,"Applies when termination is associated with a crash; not a universal SCM startup fix.");
        }
        if(security&&specific&&links.All(l=>l.Source!="Microsoft"))Ms("Microsoft event fields",AuditRoot+e.Id,"Provider-specific audit schema; check the article's Windows version and event version.");
        AddReferences(e,links,security);
        return new(title,confirmed,causes,checks,fixes,applies,related,links.DistinctBy(l=>l.Url).ToArray(),specific);
    }
    static void AddFailureChecks(EventItem e,ref string[] causes,ref string[] checks,ref string[] fixes)
    {
        bool nt=e.Id is 4625 or 4776;
        string raw=e.Id==4625&&Interpreter.Code(e.Field("SubStatus")) is >0?e.Field("SubStatus"):e.Field("Status","FailureCode","ErrorCode");var code=Interpreter.Code(raw);
        string check="",fix="",possible="";
        if((nt&&code==0xC000006A)||(!nt&&code==0x18))
        {possible="A mistyped or stale saved password is a possibility; preauthentication failure does not identify the credential-holding process.";check="Locate the matching request source and check account-using services, scheduled tasks or saved connections for an outdated credential.";fix="If a stale credential is confirmed, update that specific credential and verify that matching failures cease. A password reset alone may not remove stored old credentials.";}
        else if(nt&&code==0xC0000064){check="Verify the exact account spelling, domain and whether that account exists in the intended identity store.";fix="Correct the confirmed account/domain reference; do not create a new account just to suppress the event.";}
        else if((nt&&code==0xC0000234)||(!nt&&code==0x12)){check="Check account state and policy. Kerberos 0x12 can reflect revoked credentials, including more than a lockout.";fix="Confirm whether the account is locked, disabled or expired; resolve the documented state under your account-management procedure and trace preceding failures.";}
        else if(nt&&code==0xC0000072){check="Confirm why the account is disabled and whether the request is expected.";fix="Remove unintended account use or obtain authorization before re-enabling an account.";}
        else if(nt&&code==0xC000015B){check="Compare the recorded logon type with effective allow/deny logon rights on the destination; check the governing policy.";fix="Correct an unintended account assignment or logon-right policy through approved administration. Do not grant broad rights without confirming the requirement.";}
        else if((nt&&code is 0xC0000071 or 0xC0000224)||(!nt&&code==0x17)){check="Check password/key expiry and whether a password change is required. Consider dependent services before changing a service-account credential.";fix="Perform the authorized password/key update, update confirmed dependencies, and verify authentication afterward.";}
        else if(nt&&code==0xC0000193){check="Check account expiration and the business justification for continued use.";fix="Renew the account only if authorized, or stop the outdated account reference.";}
        else if((nt&&code==0xC0000133)||(!nt&&code==0x25)){check="Compare the actual time and configured time source on the client and authenticating server.";fix="Correct the verified time-synchronization problem and retest. Changing the time zone alone does not correct clock skew.";}
        else if(nt&&code is 0xC000006F or 0xC0000070){check="Check the account's permitted hours/workstations and any applicable authentication policy against the actual request.";fix="Use a permitted time/device or correct an unintended restriction through the governing policy.";}
        else if(nt&&code==0xC000005E){check="Check domain-controller availability, DNS, connectivity and relevant Netlogon events.";fix="Restore the confirmed connectivity/service dependency, then retest authentication.";}
        if(possible.Length>0)causes=[..causes,possible];if(check.Length>0)checks=[..checks,check];if(fix.Length>0)fixes=[..fixes,fix];
    }
    static void AddReferences(EventItem e,List<KnowledgeLink> links,bool security)
    {
        if(links.All(l=>l.Source!="Microsoft"))links.Add(Sources[0] with{Scope="Microsoft troubleshooting library; no event-specific Microsoft troubleshooting article is mapped here."});
        foreach(var (id,name) in new[]{("ultimate","Ultimate Windows Security"),("eventsentry","EventSentry")})
        {
            var index=Index.Value.Sources[id];var url=(security&&e.Id>=4600||e.Provider.Equals("Microsoft-Windows-Eventlog",StringComparison.OrdinalIgnoreCase)&&e.Id is >=1100 and <=1108)?index.Events.GetValueOrDefault(e.Id.ToString(CultureInfo.InvariantCulture)):null;
            links.Add(url is not null?new(name,$"Event {e.Id} reference",url,"Matching Windows audit event page; external reference, not an automatic root-cause diagnosis."):Sources.First(s=>s.Source==name) with{Scope="Browse this reference library; no matching event-specific entry is mapped for this provider."});
        }
        foreach(var group in e.Codes.Where(c=>c.Family!="unspecified"&&!c.Context.StartsWith("Candidate only",StringComparison.Ordinal)).Select(c=>(c.Field,c.Raw)).Distinct().Take(8))
            if(Interpreter.Code(group.Raw) is {} code)links.Add(new("EventSentry",$"Code lookup: {group.Field} = {group.Raw}",group.Field.Equals("LogonType",StringComparison.OrdinalIgnoreCase)?"https://system32.eventsentry.com/codes/field/LogonType":"https://system32.eventsentry.com/codes/lookup?query="+Uri.EscapeDataString($"0x{code:X8}"),"External numeric lookup; verify the code family against Explain. Identical numbers can have different meanings."));
    }
}
