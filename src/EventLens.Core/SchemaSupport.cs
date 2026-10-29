namespace EventLens.Core;

public static class SchemaSupport
{
    // Known versioned authentication fields. New versions must not silently inherit these interpretations.
    static readonly Dictionary<int,int[]> Reviewed=new(){[4624]=[0,1,2],[4625]=[0],[4768]=[0,2],[4769]=[0,2],[4771]=[0],[4776]=[0]};
    public static bool Unreviewed(EventItem e)=>e.Provider.Equals(Interpreter.SecurityProvider,StringComparison.OrdinalIgnoreCase)&&Reviewed.TryGetValue(e.Id,out var versions)&&!versions.Contains(e.Version);
    public static string Notice(EventItem e)=>Unreviewed(e)?$"Event version {e.Version} has not been reviewed for this authentication schema. Payload-sensitive interpretation is withheld; original provider text and XML remain available.":$"Recorded event version {e.Version}. Guidance describes documented event fields; provider messages and payload remain the evidence. Product/OS-specific applicability must be checked.";
}
