using Avalonia;
namespace EventLens.App;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(args.Length==2&&args[0]=="--diagnose")return Diagnostic.RunAsync(args[1]).GetAwaiter().GetResult();
        if(args.Length==2&&args[0]=="--benchmark")return ScaleBenchmark.RunAsync(args[1]).GetAwaiter().GetResult();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);return 0;
    }
    public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
