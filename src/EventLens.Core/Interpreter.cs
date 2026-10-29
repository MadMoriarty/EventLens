namespace EventLens.Core;

public static class Interpreter
{
    public const string SecurityProvider="Microsoft-Windows-Security-Auditing";
    public static readonly Dictionary<uint,string> NtStatus=new()
    {
        [0xC000006A]="Incorrect password",[0xC0000064]="Username does not exist",[0xC0000234]="Account locked",
        [0xC0000072]="Account disabled",[0xC000006F]="Outside permitted logon hours",[0xC000015B]="Requested logon type is not granted",
        [0xC000006D]="Invalid username or authentication information",[0xC0000071]="Password expired",[0xC0000193]="Account expired",
        [0xC0000070]="Workstation is not permitted",[0xC000005E]="No logon servers available",[0xC0000192]="Netlogon service is not started",
        [0xC0000224]="Password must be changed",[0xC0000133]="Clock difference between client and server"
    };
    public static readonly Dictionary<uint,string> KerberosStatus=new()
    {
        [0x6]="Client principal not found",[0x7]="Service principal not found",[0xC]="KDC policy rejected the request",
        [0x12]="Client credentials revoked (may include lockout, disablement or expiry)",[0x17]="Key/password expired",
        [0x18]="Invalid preauthentication information (commonly an incorrect password)",[0x19]="Additional preauthentication required",[0x25]="Clock skew is too large"
    };
    public static uint? Code(string value)
    {
        value=value.Trim();if(value.StartsWith("0x",StringComparison.OrdinalIgnoreCase))return uint.TryParse(value[2..],System.Globalization.NumberStyles.HexNumber,null,out var hex)?hex:null;
        if(uint.TryParse(value,System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out var number))return number;
        return int.TryParse(value,System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out var signed)?unchecked((uint)signed):null;
    }
    public static string Status(string value,bool kerberos=false)
    {
        var code=Code(value);if(code==null)return value.Length==0?"Code not recorded":"Unrecognized code: "+value;
        if(code==0)return "Success";
        if((kerberos?KerberosStatus:NtStatus).TryGetValue(code.Value,out var plain))return plain;
        return CodeCatalog.Lookup(kerberos?"kerberos":"ntstatus",code.Value)?.Description ?? $"Unmapped {(kerberos?"Kerberos":"Windows")} status 0x{code:X}";
    }
    static readonly Dictionary<int,(string,EventCategory)> Security=new()
    {
        [4624]=("Successful sign-in",EventCategory.SuccessfulAuthentication),[4625]=("Failed sign-in",EventCategory.FailedAuthentication),
        [4648]=("Explicit credentials used for a logon attempt",EventCategory.Other),[4634]=("Logon session ended",EventCategory.Other),[4647]=("User initiated sign-out",EventCategory.Other),
        [4740]=("Account locked out",EventCategory.Lockout),[4767]=("Account unlocked",EventCategory.AccountChange),
        [4723]=("Password change attempt",EventCategory.PasswordChange),[4724]=("Password reset attempt",EventCategory.PasswordChange),
        [4720]=("User account created",EventCategory.AccountChange),[4726]=("User account deleted",EventCategory.AccountChange),
        [4722]=("User account enabled",EventCategory.AccountChange),[4725]=("User account disabled",EventCategory.AccountChange),[4738]=("User account changed",EventCategory.AccountChange),[4781]=("Account name changed",EventCategory.AccountChange),
        [4728]=("Member added to a global security group",EventCategory.GroupChange),[4729]=("Member removed from a global security group",EventCategory.GroupChange),
        [4732]=("Member added to a local security group",EventCategory.GroupChange),[4733]=("Member removed from a local security group",EventCategory.GroupChange),
        [4756]=("Member added to a universal security group",EventCategory.GroupChange),[4757]=("Member removed from a universal security group",EventCategory.GroupChange),
        [4719]=("System audit policy changed",EventCategory.PolicyChange),[4739]=("Domain policy changed",EventCategory.PolicyChange),[4670]=("Object permissions changed",EventCategory.PolicyChange),
        [4697]=("Service installed",EventCategory.Service),[4698]=("Scheduled task created",EventCategory.ScheduledTask),[4699]=("Scheduled task deleted",EventCategory.ScheduledTask),[4702]=("Scheduled task updated",EventCategory.ScheduledTask),
    };
    public static void Apply(EventItem item)
    {
        ApplyPrimary(item);CodeCatalog.Enrich(item);
        if(!SchemaSupport.Unreviewed(item)&&PlainWords.Describe(item) is {Length:>0} plain)item.Explanation=plain+"\n\nRECORDED PROVIDER DETAILS\n"+item.Explanation;
    }
    static void ApplyPrimary(EventItem item)
    {
        item.Title=string.IsNullOrWhiteSpace(item.OriginalMessage)?$"{item.Provider} · Event {item.Id}":item.OriginalMessage.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? $"Event {item.Id}";
        item.Explanation=string.IsNullOrWhiteSpace(item.OriginalMessage)?"Provider message resources are unavailable. Inspect the original XML and event properties; no explanation has been invented.":item.OriginalMessage;
        if(SchemaSupport.Unreviewed(item)){item.Rule=SchemaSupport.Notice(item);item.Explanation+="\n\n"+item.Rule;return;}
        if(item.Provider.Equals(SecurityProvider,StringComparison.OrdinalIgnoreCase))
        {
            if(Security.TryGetValue(item.Id,out var known))
            {
                Set(item,known.Item1,known.Item2,"Microsoft Security Auditing event schema");
                if(item.Id==4625)
                {
                    string sub=item.Field("SubStatus"),status=item.Field("Status");string code=Code(sub) is >0?sub:status;
                    item.Failure=Status(code);item.Title+=$": {item.Failure}";
                    item.Explanation=$"Windows recorded a failed logon. Status: {Status(status)} ({status}); substatus: {(Code(sub)==0?"No additional failure substatus":Status(sub))} ({sub}). This describes the recorded failure, not its intent or root cause.";
                }
                if(item.Id is 4723 or 4724)
                {
                    var keywords=Code64(item.Keywords);string outcome=(keywords&0x10000000000000)!=0?"Failed":(keywords&0x20000000000000)!=0?"Successful":"Outcome not recorded";
                    item.Title+=" · "+outcome;item.Explanation+=" A password operation attempt does not establish success unless audit outcome confirms it.";
                }
                if(item.Id==4740)item.Explanation="Windows recorded an account lockout. CallerComputerName is a recorded clue when present; it does not alone prove the originating machine or root cause. Review preceding failures on relevant domain controllers and the workstation.";
                return;
            }
            if(item.Id is 4768 or 4769 or 4771 or 4776)
            {
                bool ntlm=item.Id==4776;string code=item.Field(ntlm?"Status":"Status", "FailureCode","ErrorCode");var value=Code(code);
                bool failed=item.Id==4771 || value is >0;
                Set(item,$"{(ntlm?"NTLM credential validation":item.Id==4768?"Kerberos ticket request":item.Id==4769?"Kerberos service ticket request":"Kerberos preauthentication")}: {(failed?"failed":value==0?"successful":"outcome unknown")}",failed?EventCategory.FailedAuthentication:value==0?EventCategory.SuccessfulAuthentication:EventCategory.Other,"Microsoft authentication schema; protocol-specific status mapping");
                if(failed){item.Failure=Status(code,!ntlm);item.Title+=" · "+item.Failure;}
                return;
            }
        }
        if(item.Provider.Equals("Service Control Manager",StringComparison.OrdinalIgnoreCase))
        {
            if(item.Id==7045)Set(item,"Service installed",EventCategory.Service,"Service Control Manager schema");
            else if(item.Id is 7000 or 7001 or 7009 or 7011 or 7023 or 7024 or 7031 or 7034)Set(item,"Service failure: "+item.Field("param1","ServiceName"),EventCategory.Service,"Service Control Manager schema");
        }
        else if(item.Provider.Equals("Application Error",StringComparison.OrdinalIgnoreCase)&&item.Id==1000)Set(item,"Application crash: "+item.Field("AppName","param1"),EventCategory.ApplicationCrash,"Application Error schema");
        else if((item.Provider.Equals("Microsoft-Windows-Kernel-Power",StringComparison.OrdinalIgnoreCase)&&item.Id==41)||(item.Provider.Equals("EventLog",StringComparison.OrdinalIgnoreCase)&&item.Id==6008))Set(item,"Unexpected shutdown recorded",EventCategory.Shutdown,"Kernel-Power / EventLog schema; does not establish the shutdown cause");
        else if(item.Provider.Equals("USER32",StringComparison.OrdinalIgnoreCase)&&item.Id==1074)Set(item,"Shutdown or restart initiated",EventCategory.Shutdown,"USER32 schema");
        else if(item.Provider.Equals("Microsoft-Windows-Eventlog",StringComparison.OrdinalIgnoreCase)&&item.Id==1102)Set(item,"Security audit log cleared",EventCategory.PolicyChange,"Microsoft-Windows-Eventlog event 1102 schema");
        else if(item.Provider.Equals("Microsoft-Windows-TaskScheduler",StringComparison.OrdinalIgnoreCase)&&item.Id is 101 or 103 or 201 or 203)
        {
            var code=Code(item.Field("ResultCode","ErrorValue"));
            if(item.Id!=201 || code is >0)Set(item,"Scheduled task failure: "+item.Field("TaskName"),EventCategory.ScheduledTask,"TaskScheduler operational schema; event 201 requires a nonzero result code");
        }
    }
    static ulong Code64(string value)=>ulong.TryParse(value.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?value[2..]:value,System.Globalization.NumberStyles.HexNumber,null,out var n)?n:0;
    static void Set(EventItem item,string title,EventCategory category,string rule)
    {
        item.Known=true;item.Title=title;item.Category=category;item.Rule=rule;
        item.Explanation=title+". "+(item.OriginalMessage.Length>0?item.OriginalMessage:"This label is derived from the documented provider/event schema. Provider-rendered text is unavailable; the original payload is preserved.");
    }
}
