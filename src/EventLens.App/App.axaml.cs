using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
namespace EventLens.App;
public partial class App : Application
{
    public override void Initialize()=>AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            bool preview=desktop.Args is ["--preview",_] or ["--preview-help",_];
            desktop.MainWindow=new MainWindow(preview);
            if(preview)
            {
                var window=desktop.MainWindow;
                window.Opened+=async(_,_)=>
                {
                    await Task.Delay(600);
                    if(desktop.Args![0]=="--preview-help"){window.FindControl<Avalonia.Controls.TabItem>("TroubleshootingTab")!.IsSelected=true;window.UpdateLayout();await Task.Delay(100);}
                    var output=System.IO.Path.GetFullPath(desktop.Args![1]);System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
                    using var image=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)window.Width,(int)window.Height),new Vector(96,96));image.Render(window);image.Save(output);window.Close();
                };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
