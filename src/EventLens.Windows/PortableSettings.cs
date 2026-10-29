using System.Text.Json;
using EventLens.Core;
namespace EventLens.Windows;

public sealed class PortableSettings
{
    public string Path {get;private set;}
    public string Warning {get;private set;}="";
    public PortableSettings()
    {
        string portable=System.IO.Path.Combine(AppContext.BaseDirectory,"eventlens.settings.json");
        string fallback=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EventLens","eventlens.settings.json");
        // Probe a tiny owned temporary file, never modify an existing config as a writability test.
        try{string probe=System.IO.Path.Combine(AppContext.BaseDirectory,".eventlens-write-"+Guid.NewGuid());using(File.Create(probe)){}File.Delete(probe);Path=portable;}
        catch(IOException){Path=fallback;Warning="Executable folder is read-only; settings use LocalAppData/EventLens.";}
        catch(UnauthorizedAccessException){Path=fallback;Warning="Executable folder is read-only; settings use LocalAppData/EventLens.";}
    }
    public AppSettings Load()
    {
        if(!File.Exists(Path))return new();
        try
        {
            if(new FileInfo(Path).Length>2_000_000)throw new IOException("Settings file exceeds 2 MB.");var settings=JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path))??throw new JsonException("Settings root cannot be null.");
            if(settings.Searches==null||settings.Computers==null||settings.HiddenColumns==null||settings.ColumnWidths==null||settings.Searches.Any(s=>s==null||s.Filter==null||s.Name==null)||settings.Computers.Any(c=>string.IsNullOrWhiteSpace(c)))throw new JsonException("Invalid settings structure.");
            return settings;
        }
        catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException)
        {
            Warning="Could not load settings; original file is preserved. "+e.Message;
            Path=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!,"eventlens.recovered.settings.json");return new();
        }
    }
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);string temporary=Path+"."+Guid.NewGuid()+".tmp";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(settings,new JsonSerializerOptions{WriteIndented=true}));File.Move(temporary,Path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
