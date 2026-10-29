namespace EventLens.Core;

public sealed record InvestigationPlan(string Name,string Question,string[] Channels,EventSelector[] Selectors,string[] MissingEvidence,string[] NextChecks);
public static class InvestigationPlans
{
    static EventSelector Security(params int[] ids)=>new(Interpreter.SecurityProvider,ids);
    public static IReadOnlyList<InvestigationPlan> All {get;}=[
        new("Account lockout","Which recorded failures preceded the lockout, and where did Windows record them originating?",["Security"],[Security(4740,4625,4771,4776,4767,4624,4768,4769)],
            ["Other domain controllers and the caller workstation may contain additional failures.","A caller computer or IP is a source clue; it does not identify the application holding credentials."],
            ["Inspect Status/SubStatus and compare the 15 minutes before the lockout.","Check confirmed source services, tasks and saved connections; confirm corrections before unlocking through your normal process."]),
        new("Failed sign-ins","What rejection did Windows record, and what context supports it?",["Security"],[Security(4625,4771,4776,4648,4624,4768,4769)],
            ["A missing source/process field cannot be inferred from an error code.","Successful logons and ticket requests are different operations; a nearby success is not necessarily recovery of the failed attempt."],
            ["Compare account, protocol, logon type and recorded source.","Use code-specific checks. Distinguish wrong password, disabled/expired account, restrictions and connectivity."]),
        new("Application crash","Which application failed, and what independent evidence accompanies it?",["Application"],[new("Application Error",[1000]),new("Windows Error Reporting",[1001]),new("Application Hang",[1002]),new(".NET Runtime",[1026])],
            ["The faulting module is not automatically the defective component.","An application dump and vendor diagnostics may be required to establish the failing call path."],
            ["Match executable/module paths, versions, process IDs and timestamps.","Check related WER/runtime events and supported vendor updates before choosing a fix."]),
        new("Service failure","What service operation failed, and which recorded conditions accompany it?",["System","Application"],[new("Service Control Manager",[7000,7001,7009,7011,7023,7024,7031,7034,7038,7040,7045]),new("Application Error",[1000]),new("Windows Error Reporting",[1001])],
            ["Service configuration, dependencies and account rights are not proven by a generic SCM message.","A service installation is activity evidence, not itself a failure."],
            ["Identify the service and exact startup/logon/termination error in the original message.","Correlate Application errors; inspect confirmed dependencies, binary path and execution-account configuration."]),
        new("Unexpected restart","What distinguishes an unclean restart from a recorded planned restart or Stop error?",["System"],[new("Microsoft-Windows-Kernel-Power",[41]),new("EventLog",[6005,6006,6008]),new("User32",[1074]),new("Microsoft-Windows-WER-SystemErrorReporting",[1001]),new("BugCheck",[1001])],
            ["Event 41/6008 alone cannot establish power-supply or driver failure.","Crash dumps, the preceding log interval and observed hardware/power conditions may be needed."],
            ["Compare the unexpected-shutdown records with planned shutdown and bugcheck evidence.","Check the recorded BugcheckCode and dump availability; a zero code does not establish a power fault."])
    ];
    public static string Describe(InvestigationPlan plan,IEnumerable<EventItem> events,IReadOnlyList<LogSource> sources,bool complete,IReadOnlyList<SourceIssue> issues)
    {
        var material=events.ToArray();var counts=material.GroupBy(e=>(e.Provider,e.Id)).Select(g=>$"{g.Key.Provider} / {g.Key.Id}: {g.Count():N0} recorded event(s)");
        return plan.Name+"\n\nQUESTION\n"+plan.Question+"\n\nSCOPE\n"+(complete?"Accessible query exhausted without reported source errors.":"Partial view or source errors: absence of an event does not establish absence of activity.")+"\n"+string.Join("\n",sources.Select(s=>s.Display))+"\n\nRECORDED EVIDENCE\n"+(material.Length>0?string.Join("\n",counts):"No matching records in the examined scope.")+"\n\nEXPECTED EVIDENCE NOT PRESENT IN THIS SCOPE\n"+string.Join("\n",plan.Selectors.Where(s=>!material.Any(e=>e.Provider.Equals(s.Provider,StringComparison.OrdinalIgnoreCase)&&s.Ids.Contains(e.Id))).Select(s=>s.Provider+": "+string.Join(", ",s.Ids)))+"\n\nCOVERAGE AND MISSING INFORMATION\n"+string.Join("\n",plan.MissingEvidence.Concat(issues.Select(i=>i.Source+": "+i.Message)))+"\n\nNEXT CHECKS\n"+string.Join("\n",plan.NextChecks)+"\n\nNo cause is confirmed by this summary. Review each event's Help, original message and XML. Disabled auditing, retention and clock differences may affect evidence.";
    }
}

/// <summary>Bounded summary of all captured evidence for one guided question.</summary>
public sealed class InvestigationEvidence(InvestigationPlan plan)
{
    readonly Dictionary<(string Provider,int Id),long> counts=new();
    long examined,relevant;
    public void Add(EventItem item)
    {
        examined++;var selector=plan.Selectors.FirstOrDefault(s=>s.Provider.Equals(item.Provider,StringComparison.OrdinalIgnoreCase)&&s.Ids.Contains(item.Id));if(selector==null)return;
        relevant++;var key=(selector.Provider,item.Id);counts[key]=counts.GetValueOrDefault(key)+1;
    }
    public string Report(IReadOnlyList<LogSource> sources,QuerySpec filter,SearchProgress state)=>plan.Name+"\n\nQUESTION\n"+plan.Question+
        $"\n\nCAPTURED QUERY\n{filter.From:O} to {filter.To:O}\n"+string.Join("\n",sources.Select(s=>s.Display))+
        $"\n\nALL-MATCH EVIDENCE SUMMARY\n{examined:N0} unique matching events examined; {relevant:N0} match this question's provider/event selections.\n"+string.Join("\n",counts.Select(p=>$"{p.Key.Provider} / {p.Key.Id}: {p.Value:N0}"))+
        "\n\nEXPECTED EVENT TYPES NOT PRESENT\n"+string.Join("\n",plan.Selectors.SelectMany(s=>s.Ids.Where(id=>!counts.ContainsKey((s.Provider,id))).Select(id=>s.Provider+" / "+id)))+
        "\n\nCOMPLETENESS\n"+(state.Completed&&state.Issues.Count==0?"Accessible query exhausted without reported source errors. This does not establish that auditing or retention captured all activity.":"PARTIAL: query unfinished or source errors were reported.")+
        "\n"+string.Join("\n",state.Issues.Select(i=>i.Source+": "+i.Message))+
        "\n\nMISSING INFORMATION TO CONSIDER\n"+string.Join("\n",plan.MissingEvidence)+"\n\nNEXT CHECKS\n"+string.Join("\n",plan.NextChecks)+"\n\nThis summary establishes recorded counts and coverage, not a root cause. Review event Help, payloads and original XML for the actual evidence.";
}
