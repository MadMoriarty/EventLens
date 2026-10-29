using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using EventLens.App.ViewModels;
using EventLens.Core;
using EventLens.Windows;

namespace EventLens.App;
public partial class MainWindow : Window
{
    readonly MainViewModel vm;
    bool initialized,closing;
    readonly bool preview;
    readonly DispatcherTimer debounce=new(){Interval=TimeSpan.FromMilliseconds(650)};
    public MainWindow():this(false){}
    public MainWindow(bool preview)
    {
        this.preview=preview;
        InitializeComponent();vm=new();DataContext=vm;
        vm.ScrollNewest=()=>{if(vm.Events.Count>0)ResultsGrid.ScrollIntoView(vm.Events[0],null);};
        vm.NotifyFailure=()=>{if(OperatingSystem.IsWindows())NativeSound();};
        foreach(var column in ResultsGrid.Columns)
        {
            var name=column.Header?.ToString()??"";column.IsVisible=!(preview?new AppSettings().HiddenColumns:vm.Settings.HiddenColumns).Contains(name);
            if(!preview&&vm.Settings.ColumnWidths.TryGetValue(name,out var width)&&width>=40&&width<2000)column.Width=new(width);
        }
        KeyDown+=OnKeyDown;
        Closing+=OnClosing;
        Opened+=async(_,_)=>{initialized=true;if(preview)PreviewData.Apply(vm);else await vm.SearchAsync();};
        debounce.Tick+=async(_,_)=>{debounce.Stop();if(!preview&&initialized&&!vm.IsBusy&&!vm.IsWatching)await vm.SearchAsync();};
        SearchText.TextChanged+=(_,_)=>{if(!preview&&initialized&&!vm.IsWatching){debounce.Stop();debounce.Start();}};
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool MessageBeep(uint type);
    void NativeSound()=>MessageBeep(0x30);
    void TitlePointerPressed(object? sender,PointerPressedEventArgs e)
    {
        if(e.GetCurrentPoint(this).Properties.IsLeftButtonPressed&&e.Source is not Button)
        {if(e.ClickCount==2)WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;else BeginMoveDrag(e);}
    }
    void CloseClick(object? sender,RoutedEventArgs e)=>Close();
    void MinimizeClick(object? sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    async void NavigationChanged(object? sender,SelectionChangedEventArgs e)
    {
        if(!initialized||NavigationList.SelectedItem is not string target||target==vm.Section)return;
        vm.Section=target;
        await Safe(()=>vm.NavigateAsync(target));if(target=="Custom Event Channels")await ShowSources();
    }
    async void OnKeyDown(object? sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape){vm.Cancel();e.Handled=true;}
        if(e.KeyModifiers.HasFlag(KeyModifiers.Control))switch(e.Key)
        {
            case Key.F:SearchText.Focus();SearchText.SelectAll();e.Handled=true;break;
            case Key.R:await vm.SearchAsync();e.Handled=true;break;
            case Key.O:await ImportAsync();e.Handled=true;break;
            case Key.E:await ExportAsync(vm.Events);e.Handled=true;break;
        }
    }
    async void OnClosing(object? sender,WindowClosingEventArgs e)
    {
        if(closing)return;e.Cancel=true;closing=true;debounce.Stop();
        try{if(!preview)vm.SaveColumns(ResultsGrid.Columns.Where(c=>!c.IsVisible).Select(c=>c.Header?.ToString()??"").ToList(),ResultsGrid.Columns.ToDictionary(c=>c.Header?.ToString()??"",c=>c.ActualWidth));}catch(Exception){}
        try{await vm.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4));}catch(Exception){}
        Close();
    }
    async Task Safe(Func<Task> action){try{await action();}catch(Exception e){vm.Status=NativeQuery.Friendly(e);}}
    async void ImportClick(object? sender,RoutedEventArgs e)=>await Safe(ImportAsync);
    async Task ImportAsync()
    {
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title="Open exported Windows event logs",AllowMultiple=true,FileTypeFilter=[new("Windows event logs"){Patterns=["*.evtx"]}]});
        foreach(var file in files)
        {
            var path=file.TryGetLocalPath();if(path!=null&&!vm.Imported.Any(s=>s.Path.Equals(path,StringComparison.OrdinalIgnoreCase)))vm.Imported.Add(new("offline",path,true));
        }
        if(files.Count>0){vm.Section="Imported EVTX Files";vm.SourcesChanged();await vm.SearchAsync();}
    }
    async void SourcesClick(object? sender,RoutedEventArgs e)=>await Safe(ShowSources);
    async Task ShowSources()
    {
        var dialog=NewDialog("Event sources",720,600);
        var computers=new TextBox{Text=string.Join(Environment.NewLine,vm.Computers),AcceptsReturn=true,Height=95,Watermark="One hostname per line; current Windows credentials are used"};
        var manual=new TextBox{Text=string.Join(Environment.NewLine,vm.Channels),AcceptsReturn=true,Height=90};
        var channels=new ListBox{SelectionMode=SelectionMode.Multiple,Height=210};
        var feedback=new TextBlock{Text="Choose channels on the first computer, or enter exact channel names below.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Gray};
        var discover=new Button{Content="Discover channels on first computer"};
        discover.Click+=async(_,_)=>
        {
            discover.IsEnabled=false;try{var first=Lines(computers.Text).FirstOrDefault()??"localhost";using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(10));var names=await QuerySession.ChannelsAsync(first,cancel.Token).WaitAsync(TimeSpan.FromSeconds(12));channels.ItemsSource=names;foreach(var name in vm.Channels)channels.SelectedItems?.Add(name);feedback.Text=$"{names.Count} channels found. Select multiple channels, then Use selection.";}catch(Exception ex){feedback.Text=NativeQuery.Friendly(ex);}finally{discover.IsEnabled=true;}
        };
        var select=new Button{Content="Use selected channels"};select.Click+=(_,_)=>{if(channels.SelectedItems is {} items)manual.Text=string.Join(Environment.NewLine,items.Cast<string>());};
        var unload=new Button{Content=$"Unload {vm.Imported.Count} imported EVTX file(s)"};unload.Click+=(_,_)=>{vm.Imported.Clear();vm.SourcesChanged();unload.Content="Imported files unloaded; original files unchanged";};
        var save=new Button{Content="Apply sources",Classes={"accent"}};save.Click+=async(_,_)=>
        {
            var hosts=Lines(computers.Text);var names=Lines(manual.Text);
            if(hosts.Length==0||hosts.Length>8||names.Length==0||hosts.Length*names.Length>32){feedback.Text="Choose 1–8 computers and at most 32 computer/channel combinations.";return;}
            if(hosts.Any(h=>h.Length>255||h.Any(c=>!char.IsLetterOrDigit(c)&&c is not '.' and not '-' and not ':' and not '_'))){feedback.Text="Use plain DNS names, IP addresses or localhost.";return;}
            vm.Computers=hosts.ToList();vm.Channels=names.ToList();vm.SourcesChanged();try{vm.Persist();}catch(Exception ex){feedback.Text="Could not save settings: "+ex.Message;return;}
            dialog.Close();await vm.SearchAsync();
        };
        var cancelButton=new Button{Content="Cancel"};cancelButton.Click+=(_,_)=>dialog.Close();
        dialog.Content=new ScrollViewer{Content=new StackPanel{Margin=new Thickness(22),Spacing=10,Children={new TextBlock{Text="COMPUTERS",Classes={"caption"}},computers,discover,feedback,channels,select,new TextBlock{Text="EXACT CHANNEL NAMES · one per line",Classes={"caption"}},manual,new TextBlock{Text="Security and remote channels may need additional Windows rights. EventLens uses your current identity; passwords are never collected or stored. EVTX files are opened separately.",TextWrapping=TextWrapping.Wrap,FontSize=11},unload,new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,Children={save,cancelButton}}}}};
        await dialog.ShowDialog(this);
    }
    static string[] Lines(string? value)=>(value??"").Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    void FiltersClick(object? sender,RoutedEventArgs e){AdvancedFilters.IsVisible=!AdvancedFilters.IsVisible;AdvancedFilters.IsExpanded=AdvancedFilters.IsVisible;}
    async void LockoutClick(object? sender,RoutedEventArgs e)=>await Safe(vm.InvestigateLockoutAsync);
    void SaveSearchClick(object? sender,RoutedEventArgs e)=>vm.SaveSearch();
    async void ApplySavedClick(object? sender,RoutedEventArgs e)=>await Safe(vm.ApplySavedAsync);
    void RemoveSavedClick(object? sender,RoutedEventArgs e)=>vm.RemoveSaved();
    void PanelsClick(object? sender,RoutedEventArgs e)
    {
        // Cycle three useful panel layouts without losing persisted view data.
        if(DetailsPanel.IsVisible&&NavPanel.IsVisible){DetailsPanel.IsVisible=false;}
        else if(!DetailsPanel.IsVisible&&NavPanel.IsVisible){NavPanel.IsVisible=false;}
        else{DetailsPanel.IsVisible=true;NavPanel.IsVisible=true;}
        if(DetailsPanel.Parent is Grid grid){grid.ColumnDefinitions[0].Width=NavPanel.IsVisible?new(206):new(0);grid.ColumnDefinitions[4].Width=DetailsPanel.IsVisible?new(362):new(0);}
    }
    async void ColumnsClick(object? sender,RoutedEventArgs e)
    {
        var dialog=NewDialog("Result columns",340,470);var panel=new StackPanel{Margin=new Thickness(20),Spacing=9};
        foreach(var column in ResultsGrid.Columns){var check=new CheckBox{Content=column.Header,IsChecked=column.IsVisible};check.IsCheckedChanged+=(_,_)=>column.IsVisible=check.IsChecked==true;panel.Children.Add(check);}
        var close=new Button{Content="Done"};close.Click+=(_,_)=>dialog.Close();panel.Children.Add(close);dialog.Content=panel;await dialog.ShowDialog(this);
    }
    async void ExportClick(object? sender,RoutedEventArgs e)=>await Safe(()=>ExportAsync(vm.Events));
    async void ExportCsvClick(object? sender,RoutedEventArgs e)=>await Safe(()=>ExportAsync(vm.Events,"csv"));
    async void ExportJsonClick(object? sender,RoutedEventArgs e)=>await Safe(()=>ExportAsync(vm.Events,"json"));
    void RecoverLiveClick(object? sender,RoutedEventArgs e)=>vm.RecoverLive();
    async void PreviousClick(object? sender,RoutedEventArgs e)=>await Safe(vm.PreviousPageAsync);
    async void ScanAllClick(object? sender,RoutedEventArgs e)=>await Safe(vm.CompleteSearchAsync);
    async void ExportAllJsonClick(object? sender,RoutedEventArgs e)=>await Safe(()=>ExportAllAsync(true));
    async void ExportAllCsvClick(object? sender,RoutedEventArgs e)=>await Safe(()=>ExportAllAsync(false));
    async Task ExportAllAsync(bool json)
    {
        if(vm.IsBusy||vm.ActiveQuery is not {} query){vm.Status="Run a search first and wait for its current page.";return;}
        string extension=json?"json":"zip";
        var file=await StorageProvider.SaveFilePickerAsync(new(){Title=$"Export all matches from captured query (through {query.Filter.To:O})",SuggestedFileName="eventlens-all-matches."+extension,DefaultExtension=extension,ShowOverwritePrompt=true,FileTypeChoices=[new(json?"JSON investigation report":"CSV bundle with completeness metadata"){Patterns=["*."+extension]}]});
        if(file?.TryGetLocalPath() is not {} path)return;
        if(!ReferenceEquals(query,vm.ActiveQuery)||vm.IsBusy){vm.Status="Query changed while choosing a destination. Choose Export again.";return;}
        if(path.Equals(vm.SettingsStore.Path,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Choose a separate report file; application settings cannot be replaced.");
        await vm.ExportAllAsync(path,json);
    }
    async void ExportNativeClick(object? sender,RoutedEventArgs e)=>await Safe(async()=>
    {
        if(vm.IsBusy)return;var sources=vm.SelectedSources();if(sources.Count==0)return;
        var dialog=NewDialog("Native EVTX export",720,400);var list=new ComboBox{ItemsSource=sources.Select(s=>s.Display).ToArray(),SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
        var text=new TextBlock{Text="Choose one source. Exports original Windows records using time, provider, event-ID and severity predicates only. Username, text, origin and category filters are NOT applied. Use Export ALL matches for exact filtered CSV/JSON. Message resources are not bundled; another PC may require the provider to render descriptions.",TextWrapping=TextWrapping.Wrap};
        var export=new Button{Content="Choose EVTX destination...",Classes={"accent"}};
        export.Click+=async(_,_)=>await Safe(async()=>
        {
            var source=sources[list.SelectedIndex];var filter=vm.CurrentFilter();filter=filter with {To=filter.To??DateTimeOffset.UtcNow};
            var file=await StorageProvider.SaveFilePickerAsync(new(){Title="Export source records (native filters only)",SuggestedFileName="eventlens-source.evtx",DefaultExtension="evtx",ShowOverwritePrompt=true,FileTypeChoices=[new("Windows event log"){Patterns=["*.evtx"]}]});
            if(file?.TryGetLocalPath() is not {} path)return;
            if(vm.Imported.Any(s=>System.IO.Path.GetFullPath(s.Path).Equals(System.IO.Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Imported original logs cannot be overwritten.");
            dialog.Close();await vm.RunExternalAsync(token=>NativeExport.WriteAsync(source,filter,path,token),"Exporting native EVTX... Cancel is available.");
        });
        dialog.Content=new StackPanel{Margin=new Thickness(20),Spacing=16,Children={text,list,export}};await dialog.ShowDialog(this);
    });
    async void GuidedClick(object? sender,RoutedEventArgs e)=>await Safe(async()=>
    {
        var dialog=NewDialog("Guided investigations",850,740);var plans=new ComboBox{ItemsSource=InvestigationPlans.All.Select(p=>p.Name).ToArray(),SelectedIndex=vm.ActivePlan is {} active?InvestigationPlans.All.ToList().IndexOf(active):0,HorizontalAlignment=HorizontalAlignment.Stretch};
        var text=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=430};
        void Describe()
        {
            var plan=InvestigationPlans.All[plans.SelectedIndex];
            if(vm.ActivePlan!=plan)
            {
                text.Text=plan.Name+"\n\nQUESTION\n"+plan.Question+"\n\nSTART HERE\nClick Run this investigation to collect the relevant provider/event selections. Your chosen computers and time range are retained. No existing unrelated results have been analyzed as this question's evidence.\n\nCHANNELS\n"+string.Join(", ",plan.Channels)+"\n\nNEXT CHECKS\n"+string.Join("\n",plan.NextChecks)+"\n\nINFORMATION YOU MAY NEED\n"+string.Join("\n",plan.MissingEvidence)+"\n\nAfter the search, use Analyze all matches to review the entire captured query. Event explanations, original messages and XML remain available in the main window.";return;
            }
            var query=vm.ActiveQuery;text.Text=InvestigationPlans.Describe(plan,vm.Events,vm.SelectedSources(),query!=null&&query.Completed&&query.Store.Count<=vm.Events.Count&&query.Issues.Count==0,query?.Issues??[])+"\n\nThis summary covers the DISPLAYED PAGE. Analyze all matches reviews the complete captured query.";
        }
        plans.SelectionChanged+=(_,_)=>Describe();Describe();var run=new Button{Content="Run this investigation",Classes={"accent"},IsEnabled=!vm.IsBusy};
        run.Click+=async(_,_)=>{run.IsEnabled=false;plans.IsEnabled=false;text.Text="Searching the selected sources...";try{await Safe(()=>vm.RunPlanAsync(InvestigationPlans.All[plans.SelectedIndex]));Describe();}finally{run.IsEnabled=true;plans.IsEnabled=true;}};
        var clear=new Button{Content="Clear guided event selection"};clear.Click+=(_,_)=>{vm.ClearPlan();dialog.Close();};
        var analyze=new Button{Content="Analyze all matches"};analyze.Click+=async(_,_)=>await Safe(async()=>{analyze.IsEnabled=false;try{text.Text=await vm.AnalyzeAllAsync(InvestigationPlans.All[plans.SelectedIndex]);}finally{analyze.IsEnabled=true;}});
        var copy=new Button{Content="Copy summary"};copy.Click+=async(_,_)=>{if(Clipboard!=null)await Clipboard.SetTextAsync(text.Text??"");};
        dialog.KeyDown+=(_,args)=>{if(args.Key==Key.Escape)vm.Cancel();};
        dialog.Content=new StackPanel{Margin=new Thickness(20),Spacing=12,Children={new TextBlock{Text="Choose a question. Your computers and time range are retained; event/channel filters are set for the chosen workflow.",TextWrapping=TextWrapping.Wrap},plans,text,new WrapPanel{Orientation=Orientation.Horizontal,Children={run,analyze,copy,clear}}}};await dialog.ShowDialog(this);
    });
    async void CoverageClick(object? sender,RoutedEventArgs e)
    {
        var dialog=NewDialog("References and troubleshooting",760,660);var sources=new StackPanel{Spacing=14,Margin=new Thickness(18)};
        foreach(var source in Troubleshooting.Sources)
        {
            sources.Children.Add(new TextBlock{Text=source.Source,FontSize=16,FontWeight=FontWeight.SemiBold});
            sources.Children.Add(new TextBlock{Text=source.Scope,TextWrapping=TextWrapping.Wrap});
            var open=new Button{Content=source.Title+" ↗",Tag=source.Url,HorizontalAlignment=HorizontalAlignment.Left};open.Click+=ReferenceClick;sources.Children.Add(open);
        }
        sources.Children.Add(new SelectableTextBlock{Text=Troubleshooting.Coverage,TextWrapping=TextWrapping.Wrap});
        dialog.Content=new TabControl{Items={new TabItem{Header="Reference sources",Content=new ScrollViewer{Content=sources}},new TabItem{Header="Offline code coverage",Content=new TextBox{Text=CodeCatalog.Coverage,Margin=new Thickness(18),IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap}}}};
        await dialog.ShowDialog(this);
    }
    async void ReferenceClick(object? sender,RoutedEventArgs e)=>await Safe(async()=>
    {
        if(sender is not Button{Tag:string url}||!Troubleshooting.IsAllowedReference(url))throw new InvalidOperationException("This reference URL is not supported.");
        if(!await Launcher.LaunchUriAsync(new Uri(url)))throw new InvalidOperationException("The browser could not open this reference. Copy its URL from the troubleshooting report.");
        vm.Status="Opened reference in your browser. Event contents were not included in the link.";
    });
    async void CopyGuidanceClick(object? sender,RoutedEventArgs e){if(Clipboard!=null&&vm.Selected is {} item)await Clipboard.SetTextAsync(item.Guidance.Report);}
    void ShowGuidanceClick(object? sender,RoutedEventArgs e)=>TroubleshootingTab.IsSelected=true;
    async void ExportSelectedClick(object? sender,RoutedEventArgs e){if(vm.Selected is {} item)await Safe(()=>ExportAsync([item]));}
    async Task ExportAsync(IEnumerable<EventItem> events,string extension="json")
    {
        var snapshot=events.ToArray();if(snapshot.Length==0){vm.Status="No loaded records to export.";return;}
        var jsonType=new FilePickerFileType("JSON report with original XML"){Patterns=["*.json"]};var csvType=new FilePickerFileType("CSV spreadsheet"){Patterns=["*.csv"]};
        var file=await StorageProvider.SaveFilePickerAsync(new(){Title="Export loaded investigation records",SuggestedFileName="eventlens-investigation."+extension,DefaultExtension=extension,ShowOverwritePrompt=true,FileTypeChoices=extension=="csv"?[csvType,jsonType]:[jsonType,csvType]});
        if(file?.TryGetLocalPath() is not {} path)return;
        if(!new[]{".json",".csv"}.Contains(System.IO.Path.GetExtension(path),StringComparer.OrdinalIgnoreCase)||path.Equals(vm.SettingsStore.Path,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Choose a separate .json or .csv report file. EVTX logs and application settings cannot be replaced by exports.");
        await Task.Run(()=>Exporter.WriteAsync(path,snapshot,!path.EndsWith(".csv",StringComparison.OrdinalIgnoreCase)));vm.Status=$"Exported {snapshot.Length:N0} loaded record(s). Reports contain original event data; review before sharing.";
    }
    async void CopyDetailsClick(object? sender,RoutedEventArgs e){if(Clipboard!=null)await Clipboard.SetTextAsync(vm.DetailTitle+"\n\n"+vm.Explanation+"\n\n"+vm.CodeDetails+"\n\n"+vm.Metadata);}
    async void CopyXmlClick(object? sender,RoutedEventArgs e){if(Clipboard!=null)await Clipboard.SetTextAsync(vm.RawXml);}
    async void GroupsClick(object? sender,RoutedEventArgs e)
    {
        var groups=await Task.Run(()=>Investigator.Group(vm.Events.ToArray()));var dialog=NewDialog("Repeated event groups · loaded results",1150,720);
        var list=new ListBox{ItemsSource=groups.Select((g,i)=>$"{i+1}. {g.Count:N0} × {g.Description} · {g.Identity} · {g.Source}").ToArray(),Height=180};
        var grid=DetailGrid();var summary=new TextBlock{Text="Select a group to expand its original records.",TextWrapping=TextWrapping.Wrap};
        list.SelectionChanged+=(_,_)=>{if(list.SelectedIndex>=0){var g=groups[list.SelectedIndex];grid.ItemsSource=g.Items;summary.Text=$"{g.Count} occurrences · {g.First:O} to {g.Last:O} · {g.Failure}";}};
        grid.SelectionChanged+=(_,_)=>{if(grid.SelectedItem is EventItem item)vm.Selected=item;};
        var export=new Button{Content="Export expanded group"};export.Click+=async(_,_)=>{if(list.SelectedIndex>=0)await Safe(()=>ExportAsync(groups[list.SelectedIndex].Items));};
        var layout=new Grid{RowDefinitions=new("Auto,Auto,*,Auto"),Margin=new Thickness(18),RowSpacing=10};layout.Children.Add(list);Grid.SetRow(summary,1);layout.Children.Add(summary);Grid.SetRow(grid,2);layout.Children.Add(grid);Grid.SetRow(export,3);layout.Children.Add(export);dialog.Content=layout;await dialog.ShowDialog(this);
    }
    async void CorrelateClick(object? sender,RoutedEventArgs e)
    {
        var findings=await Task.Run(()=>Investigator.Correlate(vm.Events.ToArray()));var dialog=NewDialog("Evidence correlation · loaded records",1150,740);
        var list=new ListBox{ItemsSource=findings.Select((f,i)=>$"{i+1}. {f.Title}").ToArray(),Height=150};var text=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=125,Text=findings.Count==0?"No supported patterns found in loaded records. Load more history or broaden sources; absence here does not establish absence of activity.":"Select a finding. Correlation is a temporal association, not proof of cause."};var grid=DetailGrid();
        list.SelectionChanged+=(_,_)=>{if(list.SelectedIndex>=0){var f=findings[list.SelectedIndex];text.Text=f.Confidence+"\n\n"+f.Evidence;grid.ItemsSource=f.Events;}};grid.SelectionChanged+=(_,_)=>{if(grid.SelectedItem is EventItem item)vm.Selected=item;};
        var layout=new Grid{RowDefinitions=new("Auto,Auto,*"),Margin=new Thickness(18),RowSpacing=10};layout.Children.Add(list);Grid.SetRow(text,1);layout.Children.Add(text);Grid.SetRow(grid,2);layout.Children.Add(grid);dialog.Content=layout;await dialog.ShowDialog(this);
    }
    static DataGrid DetailGrid()
    {
        var grid=new DataGrid{IsReadOnly=true,AutoGenerateColumns=false,CanUserResizeColumns=true,CanUserSortColumns=true};
        foreach(var (title,path,width) in new[]{("Timestamp (local)","TimestampDisplay",175),("Description","Title",320),("Account","User",180),("Origin","Origin",150),("Computer","Computer",140),("Event ID","Id",70)})grid.Columns.Add(new DataGridTextColumn{Header=title,Binding=new Avalonia.Data.Binding(path),Width=new(width)});
        return grid;
    }
    async void AboutClick(object? sender,RoutedEventArgs e)=>await Info("About EventLens","EventLens 1.3\n\nA portable, read-only Windows Event Log investigator.\n\nDeterministic provider-specific explanations; original event XML is always retained. Counts and correlations cover loaded records only. No service, cloud connection, telemetry or account registration.\n\nSettings: "+vm.SettingsStore.Path+"\n\nRemote access uses current Windows credentials and Event Log RPC. Domain-controller scenarios require validation in your environment. The executable is unsigned.\n\nSource and license notices are included in the portable distribution.");
    async void ShortcutsClick(object? sender,RoutedEventArgs e)=>await Info("Keyboard shortcuts","Ctrl+F · Focus text search\nCtrl+R · Run query again\nCtrl+O · Open EVTX files\nCtrl+E · Export loaded results\nEsc · Cancel query\nAlt+F4 · Close\n\nColumn headers sort loaded results only. Next page continues the existing native query. Historical results are disk-cached and paged. Scan all matches exhausts accessible history. Export ALL matches writes the captured query beyond the visible page. Live display remains bounded.\n\nUser search matches exact recorded names or SIDs, case-insensitively. DOMAIN\\user does not assume NetBIOS/DNS domain aliases.\n\nNew live records are highlighted by failure/lockout labels; optional sound is off by default. Live filters are captured when watching starts.");
    async Task Info(string title,string text){var dialog=NewDialog(title,660,480);dialog.Content=new TextBox{Margin=new Thickness(20),Text=text,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};await dialog.ShowDialog(this);}
    static Window NewDialog(string title,int width,int height)=>new(){Title=title,Width=width,Height=height,MinWidth=300,MinHeight=250,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush.Parse("#191B21"),FontFamily=new("Segoe UI"),FontSize=12};
}
