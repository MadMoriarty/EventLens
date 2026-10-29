using System.Diagnostics.Eventing.Reader;
using EventLens.Core;

namespace EventLens.Windows;

public static class NativeExport
{
    public static Task WriteAsync(LogSource source,QuerySpec filter,string path,CancellationToken token)=>Task.Run(()=>
    {
        if(!Path.GetExtension(path).Equals(".evtx",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose an .evtx destination.");
        if(source.IsFile&&Path.GetFullPath(path).Equals(Path.GetFullPath(source.Path),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("The original EVTX must remain unchanged.");
        string temp=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,".eventlens-"+Guid.NewGuid()+".evtx");
        try
        {
            token.ThrowIfCancellationRequested();using var session=SourceCursor.Local(source.Computer)||source.IsFile?new EventLogSession():new EventLogSession(source.Computer);
            using var cancel=token.Register(()=>{try{session.CancelCurrentOperations();}catch(ObjectDisposedException){}catch(EventLogException){}});
            session.ExportLog(source.Path,source.IsFile?PathType.FilePath:PathType.LogName,NativeQuery.Build(filter),temp);
            token.ThrowIfCancellationRequested();File.Move(temp,path,true);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    },token);
}
