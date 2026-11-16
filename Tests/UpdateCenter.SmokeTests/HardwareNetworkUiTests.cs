using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using UpdateCenter;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class HardwareNetworkUiTests
{
    public static void Run()
    {
        Exception? failure=null;
        var thread=new Thread(()=>{try{Verify();}catch(Exception ex){failure=ex;}});
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine("PASS Hardware/LAN UI: IT/EN, light/dark, 1240x790 and 720x500, large text, empty/500 results, offline/error/denied, bounded row recycling, eligibility, scope, risk selection, busy guards and fingerprint validation. No hardware queries, sockets, installs or remote commands.");
    }
    private static void Verify()
    {
        JsonStorage.SaveSettings(new AppSettings{ScanAtStartup=false,AutomaticScanInterval="Off",CheckAppUpdatesAutomatically=false,NotifyWhenUpdatesAreAvailable=false});
        var app=new App();app.InitializeComponent();
        LocalizationService.Initialize("it");ThemeService.Apply("Chiaro");TypographyService.Apply("Grande");
        var w=new MainWindow();
        try
        {
            _=new WindowInteropHelper(w).EnsureHandle();w.Visibility=Visibility.Hidden;
            // Do not Show or pump the dispatcher: Loaded, timers, hardware and network I/O stay inactive.
            var vm=(MainViewModel)w.DataContext;
            foreach(var locale in new[]{"it","en"})
            foreach(var theme in new[]{"Chiaro","Scuro"})
            {
                vm.Settings.LanguageMode=locale;LocalizationService.Initialize(locale);ThemeService.Apply(theme);
                Call(w,"UpdateAppearanceTheme");Call(w,"UpdateLanguageChoices");LocalizationService.ApplyTo(w);
                foreach(var state in new[]{"empty","populated","denied"})
                {
                    HardwareNetworkUiFixture.Populate(vm,state,500);
                    foreach(var size in new[]{new Size(1240,790),new Size(720,500)})
                    {
                        w.Width=size.Width;w.Height=size.Height;w.Measure(size);w.Arrange(new Rect(size));Call(w,"ApplyResponsiveLayout");
                        Show(w,"SystemInfoPage","Hardware");
                        if(state=="populated") VerifyHardwareBars(w);
                        VerifyGrid(w,"StorageGrid",state=="empty"?0:500,"StorageEmptyState");
                        if(state=="populated") VerifyStorageWheel(w);
                        Check(Grid.GetRow(Find<FrameworkElement>(w,"HardwareSecondaryDetails"))==(size.Width<1000?1:0),"Hardware columns do not adapt");
                        Show(w,"NetworkPage","Gestione rete");
                        VerifyGrid(w,"NetworkDevicesGrid",state=="empty"?0:50,"NetworkDevicesEmptyState");
                        VerifyGrid(w,"NetworkResultsGrid",state=="empty"?0:500,"NetworkResultsEmptyState");
                        Check(Find<UIElement>(w,"NetworkStatusBar").Visibility==Visibility.Visible,"LAN error/status hidden in short window");
                        var scroll=Find<ScrollViewer>(w,"NetworkScrollViewer");scroll.ScrollToEnd();w.UpdateLayout();
                        var action=Find<Button>(w,"NetworkUpdateSelectedButton");
                        var bounds=action.TransformToAncestor(w).TransformBounds(new Rect(action.RenderSize));
                        Check(bounds.Right<=size.Width+1 && bounds.Left>=0,"Remote update action clipped horizontally");
                        Check(action.FocusVisualStyle is not null,"Remote update keyboard focus missing");
                        Check(action.IsEnabled==vm.Network.CanUpdateSelected,"Remote update eligibility binding changed");
                    }
                    if(state=="populated") VerifySelection(w,vm);
                    if(state=="denied") Check(!vm.Network.CanUpdateSelected,"Denied fixture enables remote updates");
                }
                VerifyDialogs(vm);
            }
        }
        finally{w.Close();app.Shutdown();}
    }
    private static void VerifyHardwareBars(MainWindow w)
    {
        var bars=Visuals<ProgressBar>(Find<ScrollViewer>(w,"SystemInfoPage"))
            .Where(p=>BindingOperations.GetBinding(p,ProgressBar.ValueProperty)?.Path.Path is "HardwareInfo.CpuUsage" or "HardwareInfo.RamUsage" or "HardwareInfo.GpuUsage").ToArray();
        Check(bars.Length==3,"Hardware usage bars missing");
        foreach(var bar in bars)
        {
            bar.ApplyTemplate(); w.UpdateLayout();
            var track=bar.Template.FindName("PART_Track",bar) as FrameworkElement;
            var indicator=bar.Template.FindName("PART_Indicator",bar) as FrameworkElement;
            var background=bar.Template.FindName("UsageTrack",bar) as Border;
            Check(track is not null && indicator is not null && Math.Abs(track.ActualHeight-6)<0.01 && Math.Abs(indicator.ActualHeight-6)<0.01,
                $"Hardware track/indicator heights are {track?.ActualHeight}/{indicator?.ActualHeight}; expected 6");
            Check(background is not null && Math.Abs(background.ActualHeight-6)<0.01,"Hardware background height is not 6");
            Check(track!.ActualWidth>0 && Math.Abs(indicator!.ActualWidth-track.ActualWidth*bar.Value/bar.Maximum)<2,
                "Hardware indicator width does not match its usage value");
            Check(Equals(background!.Background,bar.Background) && indicator is Border indicatorBorder && Equals(indicatorBorder.Background,bar.Foreground),
                "Hardware usage colors differ from the control colors");
        }
    }
    private static void VerifyStorageWheel(MainWindow w)
    {
        var page=Find<ScrollViewer>(w,"SystemInfoPage");
        var grid=Find<DataGrid>(w,"StorageGrid");
        page.ScrollToTop(); w.UpdateLayout();
        Check(page.ScrollableHeight>0,"Hardware page must be scrollable for wheel test");
        var inner=Visuals<ScrollViewer>(grid).First();
        var before=inner.VerticalOffset;
        var wheel=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120) { RoutedEvent=UIElement.PreviewMouseWheelEvent };
        grid.RaiseEvent(wheel); w.UpdateLayout();
        Check(wheel.Handled && page.VerticalOffset>0,"Wheel over storage grid did not scroll hardware page");
        Check(inner.VerticalOffset==before,"Wheel scrolled storage grid as well as hardware page");
    }
    private static void VerifyGrid(MainWindow w,string name,int expected,string empty)
    {
        var g=Find<DataGrid>(w,name);w.UpdateLayout();
        Check(g.Items.Count==expected,$"{name}: count {g.Items.Count}, expected {expected}");
        Check(Find<UIElement>(w,empty).Visibility==(expected==0?Visibility.Visible:Visibility.Collapsed),$"{name}: empty state");
        Check(g.EnableRowVirtualization && g.EnableColumnVirtualization && ScrollViewer.GetCanContentScroll(g),$"{name}: virtualization disabled");
        Check(VirtualizingPanel.GetVirtualizationMode(g)==VirtualizationMode.Recycling,$"{name}: recycling disabled");
        Check(AutomationProperties.GetName(g).Length>0,$"{name}: missing accessible name");
        Check(g.Columns.All(c=>c.MinWidth>=50),$"{name}: unbounded column compression");
        if(expected==0)return;
        g.ScrollIntoView(g.Items[expected-1]);w.UpdateLayout();
        var rows=Visuals<DataGridRow>(g).ToList();
        Check(rows.Count>0 && rows.Count<80,$"{name}: realized {rows.Count} rows");
        Check(rows.All(r=>r.ActualHeight<160),$"{name}: oversized rows");
        Check(Visuals<DataGridCell>(g).All(c=>c.Padding.Left==12),$"{name}: global style masks Fluent spacing");
        g.ScrollIntoView(g.Items[0]);w.UpdateLayout();
    }
    private static void VerifySelection(MainWindow w,MainViewModel vm)
    {
        var n=vm.Network;
        var converter=new ManagementTextConverter();
        Check((string)converter.Convert(["Tutti i dispositivi"],typeof(string),"",System.Globalization.CultureInfo.InvariantCulture)==LocalizationService.Text("Tutti i dispositivi","All devices"),"LAN scope label not translated");
        if(LocalizationService.IsEnglish)
        {
            var resultGrid=Find<DataGrid>(w,"NetworkResultsGrid");
            var labels=Visuals<TextBlock>(resultGrid).Select(t=>t.Text).ToArray();
            Check(!labels.Any(t=>t.Contains(" aggiornamenti · ")),"LAN group header remains Italian");
            Check(n.ScanResults.First(x=>x.RequiresRiskConfirmation).PriorityLabel=="Confirm","LAN priority label remains Italian");
        }
        n.DeselectVisibleUpdates();n.SelectVisibleUpdates();
        Check(n.ScanResults.Where(x=>!x.CanInstall||x.RequiresRiskConfirmation).All(x=>!x.IsSelected),"Bulk selection bypasses install/risk guards");
        Check(n.GetSelectedUpdatesForConfirmation().Count>0,"Eligible test selection missing");
        n.SelectedResultScope=n.ResultScopes.First(x=>!x.IsAll);
        Check(n.ResultsView.Cast<object>().Count()==10,"Device result filter changed");
        n.SelectedResultScope=n.ResultScopes.First(x=>x.IsAll);
        var blocked=n.ScanResults.First(x=>!x.CanInstall);
        var grid=Find<DataGrid>(w,"NetworkResultsGrid");grid.ScrollIntoView(blocked);w.UpdateLayout();
        var row=Visuals<DataGridRow>(grid).FirstOrDefault(r=>ReferenceEquals(r.Item,blocked));
        Check(row is not null && Visuals<CheckBox>(row).Any(c=>!c.IsEnabled),"Noninstallable remote row checkbox enabled");
        HardwareNetworkUiFixture.Set(n,"IsBusy",true);w.UpdateLayout();
        Check(!Find<Button>(w,"NetworkUpdateSelectedButton").IsEnabled && !n.CanDiscover && !n.CanScanTargets,"Busy guards changed");
        HardwareNetworkUiFixture.Set(n,"IsBusy",false);
        n.ApplyRiskDecision(n.ScanResults.Where(x=>x.RequiresRiskConfirmation).ToList(),false);
        Check(n.ScanResults.Where(x=>x.RequiresRiskConfirmation).All(x=>!x.RiskConfirmed&&!x.IsSelected),"Risk exclusion changed");
    }
    private static void VerifyDialogs(MainViewModel vm)
    {
        var fingerprint=(Window)Activator.CreateInstance(typeof(AgentFingerprintWindow),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{"192.0.2.1:47382"},null)!;
        var box=Find<TextBox>(fingerprint,"FingerprintBox");var confirm=Find<Button>(fingerprint,"ConfirmButton");
        box.Text="invalid";Check(!confirm.IsEnabled,"Invalid fingerprint accepted");
        box.Text=new string('A',64);Check(confirm.IsEnabled,"Valid fingerprint rejected");fingerprint.Close();
        var pairing=new NetworkPairingWindow(vm.Network,vm.Network.Agents.FirstOrDefault()??new NetworkAgentItem());
        LocalizationService.ApplyTo(pairing);
        var code=Find<TextBox>(pairing,"PairingCodeBox");
        code.Text="123";Check(!vm.Network.CanPair,"Short pairing code enabled");
        Check(AutomationProperties.GetLabeledBy(code)==Find<TextBlock>(pairing,"PairingCodeLabel"),"Pairing input lost its accessible label");
        Check(Find<TextBlock>(pairing,"PairingCodeLabel").Text==LocalizationService.Translate("Codice temporaneo"),"Pairing accessible label not localized");
        Check(Find<TextBlock>(pairing,"PairingAddressLabel").Text==LocalizationService.Translate("Indirizzo IP"),"IP accessible label not localized");
        Check(Find<TextBlock>(pairing,"PairingPortLabel").Text==LocalizationService.Translate("Porta"),"Port accessible label not localized");
        pairing.Close();
        new AgentSetupWindow().Close();new ConnectionRequestWindow(Guid.NewGuid()).Close();
    }
    private static void Show(MainWindow w,string page,string title)
    {
        Call(w,"ShowPage",Find<UIElement>(w,page),title);
        foreach(var name in new[]{"_hardwareTimer","_networkStatusTimer"}) ((DispatcherTimer)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(w)!).Stop();
        w.UpdateLayout();
    }
    private static T Find<T>(FrameworkElement w,string name) where T:class =>(T)w.FindName(name);
    private static void Call(object o,string name,params object[] args)=>HardwareNetworkUiFixture.Invoke(o,name,args);
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T:DependencyObject
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var c=VisualTreeHelper.GetChild(root,i);if(c is T t)yield return t;foreach(var child in Visuals<T>(c))yield return child;}}
}
