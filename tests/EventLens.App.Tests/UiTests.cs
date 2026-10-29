using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media;
using EventLens.App;
using EventLens.App.ViewModels;
using EventLens.Core;
using EventLens.Windows;
using Xunit;
[assembly:AvaloniaTestApplication(typeof(EventLens.App.Tests.TestApp))]
namespace EventLens.App.Tests;
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<global::EventLens.App.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
public class UiTests
{
    [AvaloniaFact]public void GuidedQuestionIntroducesItsScopeBeforeClaimingToAnalyzeExistingRows()
    {
        var window=new MainWindow(true);window.Show();Dispatcher.UIThread.RunJobs();
        var button=window.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()?.StartsWith("Guided investigations")==true);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
        var dialog=Assert.Single(window.OwnedWindows);var text=dialog.GetVisualDescendants().OfType<TextBox>().Single(t=>t.IsReadOnly&&t.AcceptsReturn);
        Assert.Contains("START HERE",text.Text);Assert.DoesNotContain("RECORDED EVIDENCE",text.Text);dialog.Close();window.Close();
    }
    [AvaloniaFact]public void FullSearchAndExportActionsAreActuallyReachableInTheWindow()
    {
        var window=new MainWindow(true);window.Show();Dispatcher.UIThread.RunJobs();
        Assert.Equal("Scan all matches",window.FindControl<Button>("ScanAllButton")!.Content);
        var flyout=Assert.IsType<MenuFlyout>(window.FindControl<Button>("ExportEventsButton")!.Flyout);
        var headers=flyout.Items.OfType<MenuItem>().Select(i=>i.Header?.ToString()??"").ToArray();Assert.Contains(headers,h=>h.Contains("ALL matching"));Assert.Contains(headers,h=>h.Contains("CSV bundle"));Assert.Contains(headers,h=>h.Contains("native EVTX"));window.Close();
    }
    [AvaloniaFact]public async Task SyntheticPreviewNeverStartsAutomaticQueriesWhenTextBindingChanges()
    {
        var window=new MainWindow(true);window.Show();Dispatcher.UIThread.RunJobs();var vm=(MainViewModel)window.DataContext!;vm.Text="fixture search";
        await Task.Delay(800);Dispatcher.UIThread.RunJobs();Assert.Equal(28,vm.Events.Count);Assert.Contains("SYNTHETIC",vm.Status);window.Close();
    }
    [AvaloniaFact]public void HelpTabShowsOfflineGuidanceAndAllReferenceSourcesAndTracksSelection()
    {
        var window=new MainWindow(true);window.Show();Dispatcher.UIThread.RunJobs();var vm=(MainViewModel)window.DataContext!;
        window.FindControl<TabItem>("TroubleshootingTab")!.IsSelected=true;Dispatcher.UIThread.RunJobs();
        Assert.Contains("WHAT TO CHECK NEXT",window.FindControl<SelectableTextBlock>("GuidanceText")!.Text);
        var links=window.FindControl<ItemsControl>("ReferenceLinks")!.ItemsSource!.Cast<KnowledgeLink>().ToArray();Assert.Equal(3,links.Select(l=>l.Source).Distinct().Count());
        vm.Selected=vm.Events.Single(e=>e.Id==4740);Dispatcher.UIThread.RunJobs();Assert.Contains("Trace an account lockout",window.FindControl<SelectableTextBlock>("GuidanceText")!.Text);window.Close();
    }
    [AvaloniaFact]public void WarningColorCoversEveryRealizedCellIncludingSelection()
    {
        var window=new MainWindow(true);window.Show();var vm=(MainViewModel)window.DataContext!;vm.Events.Clear();
        var warning=new EventItem{Id=123,Level=3,Title="Warning fixture",Computer="TestPC",Provider="Fixture",Timestamp=DateTimeOffset.UtcNow};vm.Events.Add(warning);vm.Selected=warning;
        Dispatcher.UIThread.RunJobs();window.UpdateLayout();var grid=window.FindControl<DataGrid>("ResultsGrid")!;
        var row=Assert.Single(grid.GetVisualDescendants().OfType<DataGridRow>(),r=>ReferenceEquals(r.DataContext,warning));Assert.True(row.IsSelected);
        var cells=row.GetVisualDescendants().OfType<DataGridCell>().ToArray();Assert.True(cells.Length>=7);
        var texts=cells.SelectMany(c=>c.GetVisualDescendants().OfType<TextBlock>()).Where(t=>!string.IsNullOrEmpty(t.Text)).ToArray();Assert.True(texts.Length>=7);
        Assert.All(texts,t=>Assert.Equal(Color.Parse("#F3A35C"),Assert.IsAssignableFrom<ISolidColorBrush>(t.Foreground).Color));
        Assert.Equal("Export events ▾",window.FindControl<Button>("ExportEventsButton")!.Content);Assert.IsType<MenuFlyout>(window.FindControl<Button>("ExportEventsButton")!.Flyout);window.Close();
    }
    [AvaloniaFact]public void ThemeAccentIsTealBlueRegardlessOfWindowsAccent()
    {
        Assert.True(Application.Current!.TryGetResource("SystemAccentColor",Avalonia.Styling.ThemeVariant.Dark,out var value));Assert.Equal(Color.Parse("#289FC1"),Assert.IsType<Color>(value));
    }
    [AvaloniaFact]public void PreviewShowsRealControlsAndSyntheticMarker()
    {
        var window=new MainWindow(true);window.Show();Dispatcher.UIThread.RunJobs();var vm=(MainViewModel)window.DataContext!;
        Assert.Equal(28,vm.Events.Count);Assert.Contains("SYNTHETIC",vm.Status);Assert.Contains("Incorrect password",vm.DetailTitle);
        Assert.Equal(10,window.FindControl<DataGrid>("ResultsGrid")!.Columns.Count);window.Close();
    }
    [AvaloniaFact]public void NavigationSelectionRoutesToActualSource()
    {
        var window=new MainWindow(true);window.Show();Dispatcher.UIThread.RunJobs();var vm=(MainViewModel)window.DataContext!;
        window.FindControl<ListBox>("NavigationList")!.SelectedItem="System";Dispatcher.UIThread.RunJobs();Assert.Equal("System",vm.Section);Assert.Equal(new[]{"System"},vm.Channels);window.Close();
    }
    [AvaloniaFact]public void ProgrammaticInvestigationSectionDoesNotResetSources()
    {
        var window=new MainWindow(true);window.Show();Dispatcher.UIThread.RunJobs();var vm=(MainViewModel)window.DataContext!;
        vm.Channels=["Custom/Operational"];vm.Section="All Events";Dispatcher.UIThread.RunJobs();Assert.Equal("Custom/Operational",Assert.Single(vm.Channels));window.Close();
    }
    [AvaloniaFact]public async Task NamedFilterPersistsRelativeRangeAndSourceScope()
    {
        var vm=new MainViewModel();vm.SearchName="Test relative filter "+Guid.NewGuid();vm.Range="Last 7 days";vm.Computers=["DC01"];vm.Channels=["Security"];vm.Identity="CONTOSO\\jsmith";vm.SaveSearch();
        var saved=vm.Saved.Last();Assert.Equal("Last 7 days",saved.TimeRange);Assert.Equal("DC01",Assert.Single(saved.Computers!));Assert.Equal("CONTOSO\\jsmith",saved.Filter.Identity);vm.SelectedSaved=saved;vm.RemoveSaved();await vm.DisposeAsync();
    }
    [Fact]public void NativeQueryContainsOnlySupportedSafePredicates()
    {
        var spec=new QuerySpec{From=DateTimeOffset.Parse("2026-10-22T12:00:00Z"),EventIds=[4625,4740],Provider="Provider'with\"quotes",Identity="some'account",MaximumLevel=3};string query=NativeQuery.Build(spec);
        Assert.Contains("EventID=4625 or EventID=4740",query);Assert.DoesNotContain("some'account",query);Assert.DoesNotContain("Provider'with",query);Assert.Contains("Level <= 3",query);
        Assert.Throws<ArgumentException>(()=>NativeQuery.Build(new(){EventIds=Enumerable.Range(0,21).ToArray()}));
    }
    [Fact]public void NativeQueryTimestampDoesNotUseRegionalCalendar()
    {
        var original=System.Globalization.CultureInfo.CurrentCulture;
        try{System.Globalization.CultureInfo.CurrentCulture=new("th-TH");Assert.Contains("2026-10-22",NativeQuery.Build(new(){From=new DateTimeOffset(2026,10,22,0,0,0,TimeSpan.Zero)}));}finally{System.Globalization.CultureInfo.CurrentCulture=original;}
    }
}
