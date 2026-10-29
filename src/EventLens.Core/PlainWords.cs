namespace EventLens.Core;

public static class PlainWords
{
    public static string Describe(EventItem e)
    {
        string account=e.User=="—"?"an account whose name was not recorded":e.User;
        string source=e.Origin.Length>0?"Recorded source: "+e.Origin+".":"No usable source workstation/IP was recorded in the supported fields.";
        if(e.Category==EventCategory.FailedAuthentication)
            return $"Windows recorded an unsuccessful authentication operation for {account}. "+(e.Failure.Length>0?"Recorded rejection: "+e.Failure+". ":"A specific rejection reason is unavailable. ")+source+" This does not identify the application holding credentials or establish why the request was made.";
        if(e.Category==EventCategory.SuccessfulAuthentication)
            return $"Windows recorded a successful authentication operation for {account}. "+(e.Id==4624?"The logon type describes the session kind. ":"Credential validation or a Kerberos ticket request is not itself proof of an interactive desktop session. ")+source;
        if(e.Category==EventCategory.Lockout)return $"Windows recorded that {account} was locked out. "+source+" This event does not establish which program supplied failed credentials. Compare preceding failures across the relevant controllers and source computer.";
        if(e.Category==EventCategory.ApplicationCrash)
        {
            string app=e.Field("AppName","FaultingApplicationName"),module=e.Field("ModuleName","FaultingModuleName");
            return "Windows recorded an application crash."+(app.Length>0?" Recorded application: "+app+".":"")+(module.Length>0?" Recorded faulting module: "+module+".":"")+" A module named in a crash is a clue, not proof that the module itself is defective.";
        }
        if(e.Category==EventCategory.Shutdown&&e.Id is 41 or 6008)return "Windows recorded that the previous shutdown was not clean. This can follow a crash, power interruption, forced power-off or hang. This event alone cannot distinguish those causes.";
        if(e.Category==EventCategory.Service)
        {
            string name=e.Field("ServiceName","param1");return (e.Id is 7045 or 4697?"A service installation was recorded.":"Windows recorded a service operation or failure.")+(name.Length>0?" Recorded service: "+name+".":"")+" Inspect the exact operation and service-specific error before selecting a fix.";
        }
        return "";
    }
}
