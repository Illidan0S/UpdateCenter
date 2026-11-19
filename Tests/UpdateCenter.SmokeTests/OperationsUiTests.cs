using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using UpdateCenter;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class OperationsUiTests
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine("PASS operations UI: IT/EN, light/dark, empty/500 rows, large text, 1240x790/720x500, filters, selection guards, busy state, row recycling, localized actions and history persistence. No scan/install/repair executed.");
    }

    private static void Verify()
    {
        JsonStorage.SaveSettings(new AppSettings { ScanAtStartup=false, AutomaticScanInterval="Off", CheckAppUpdatesAutomatically=false, NotifyWhenUpdatesAreAvailable=false });
        var app = new App(); app.InitializeComponent();
        LocalizationService.Initialize("it"); ThemeService.Apply("Chiaro"); TypographyService.Apply("Grande");
        var window = new MainWindow();
        try
        {
            _ = new WindowInteropHelper(window).EnsureHandle(); window.Visibility=Visibility.Hidden;
            // No Show/Run/dispatcher pump: the application's Loaded work never runs.
            var vm = (MainViewModel)window.DataContext;
            foreach (var locale in new[] { "it", "en" })
            foreach (var theme in new[] { "Chiaro", "Scuro" })
            {
                vm.Settings.LanguageMode=locale;
                LocalizationService.Initialize(locale); ThemeService.Apply(theme);
                Invoke(window,"UpdateAppearanceTheme"); Invoke(window,"UpdateLanguageChoices");
                LocalizationService.ApplyTo(window);
                foreach (var count in new[] { 0, 500 })
                {
                    OperationsUiFixture.Populate(vm,count);
                    foreach (var size in new[] { new Size(1240,790), new Size(720,500) })
                    {
                        window.Width=size.Width; window.Height=size.Height;
                        window.Measure(size); window.Arrange(new Rect(size)); Invoke(window,"ApplyResponsiveLayout");
                        foreach (var (page, gridName, title, empty) in new[] {
                            ("UpdatesPage","UpdatesGrid","Aggiornamenti","UpdatesEmptyState"),
                            ("HardwarePage","DriverInventoryGrid","Driver e chipset","DriverInventoryEmptyState"),
                            ("HistoryPage","HistoryGrid","Cronologia","HistoryEmptyState") })
                        {
                            Invoke(window,"ShowPage",Find<UIElement>(window,page),title); window.UpdateLayout();
                            var grid=Find<DataGrid>(window,gridName);
                            Check(grid.Items.Count==count,$"{gridName}: lost rows");
                            Check(Find<UIElement>(window,empty).Visibility == (count==0 ? Visibility.Visible : Visibility.Collapsed),$"{empty}: wrong state");
                            Check(grid.EnableRowVirtualization && grid.EnableColumnVirtualization && ScrollViewer.GetCanContentScroll(grid),$"{gridName}: virtualization disabled");
                            Check(VirtualizingPanel.GetVirtualizationMode(grid)==VirtualizationMode.Recycling,$"{gridName}: recycling disabled");
                            Check(AutomationProperties.GetName(grid).Length>0,$"{gridName}: accessible name missing");
                            Check(grid.Columns.Where(c=>c.Header?.ToString()?.Length>0).All(c=>c.SortMemberPath.Length>0 || c is DataGridBoundColumn || !c.CanUserSort),$"{gridName}: advertised sort unavailable");
                            if(count>0)
                            {
                                grid.ScrollIntoView(grid.Items[count-1]); window.UpdateLayout();
                                var rows=Visuals<DataGridRow>(grid).Count();
                                Check(rows>0 && rows<80,$"{gridName}: expected bounded realized rows, got {rows}");
                                Check(Visuals<DataGridCell>(grid).All(c=>c.Padding.Left==12),$"{gridName}: application styles override scoped cell spacing");
                                grid.ScrollIntoView(grid.Items[0]); window.UpdateLayout();
                                if (gridName == "UpdatesGrid")
                                    Check(Visuals<DataGridRow>(grid).All(row=>row.ActualHeight<160),"Narrow updates table squeezes a badge into an oversized row");
                            }
                            if(page=="UpdatesPage")
                            {
                                var install=Find<Button>(window,"InstallUpdatesButton");
                                var bounds=install.TransformToAncestor(window).TransformBounds(new Rect(install.RenderSize));
                                Check(bounds.Right<=size.Width+1 && bounds.Bottom<=size.Height+1,"Install action clipped");
                            }
                        }
                    }
                    if(count>0) VerifyInteractions(window,vm);
                }
            }
        }
        finally { window.Close(); app.Shutdown(); }
    }

    private static void VerifyInteractions(MainWindow window, MainViewModel vm)
    {
        Invoke(window,"ShowPage",Find<UIElement>(window,"UpdatesPage"),"Aggiornamenti"); window.UpdateLayout();
        var updates=Find<DataGrid>(window,"UpdatesGrid");
        var type=Find<ComboBox>(window,"UpdateTypeChoice");
        type.SetCurrentValue(Selector.SelectedValueProperty,"Errori");
        Check(updates.Items.Count==100 && updates.Items.Cast<UpdateItem>().All(x=>x.CanRetry),"Error filter changed");
        type.SetCurrentValue(Selector.SelectedValueProperty,"Tutti");
        Find<TextBox>(window,"UpdateSearchBox").SetCurrentValue(TextBox.TextProperty,"QA 0000");
        Check(updates.Items.Count==1,"Search binding changed");
        Find<TextBox>(window,"UpdateSearchBox").SetCurrentValue(TextBox.TextProperty,"");
        Find<Button>(window,"SelectUpdatesButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(vm.Updates.Where(x=>!x.CanInstall || x.RequiresRiskConfirmation).All(x=>!x.IsSelected),"Bulk select bypassed eligibility/risk safeguards");
        Check(vm.Updates.Where(x=>x.CanInstall && !x.RequiresRiskConfirmation).All(x=>x.IsSelected),"Bulk select lost eligible rows");
        Find<Button>(window,"DeselectUpdatesButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(vm.SelectedCount==0 && !Find<Button>(window,"InstallUpdatesButton").IsEnabled,"Empty selection enables installation");
        updates.ScrollIntoView(vm.Updates[0],updates.Columns[0]); window.UpdateLayout();
        var checkbox=Visuals<CheckBox>(updates).First(x=>ReferenceEquals(x.DataContext,vm.Updates[0]));
        checkbox.SetCurrentValue(ToggleButton.IsCheckedProperty,true);
        Check(vm.Updates[0].IsSelected && Find<Button>(window,"InstallUpdatesButton").IsEnabled,"Checkbox two-way selection broken");
        OperationsUiFixture.Set(vm,"IsBusy",true); OperationsUiFixture.Set(vm,"IsScanRunning",true);
        Check(!Find<Button>(window,"InstallUpdatesButton").IsEnabled,"Busy state permits installation");
        OperationsUiFixture.Set(vm,"IsBusy",false); OperationsUiFixture.Set(vm,"IsScanRunning",false);
        var error=vm.Updates[3];
        updates.ScrollIntoView(error,updates.Columns[^1]); window.UpdateLayout();
        var retry=Visuals<Button>(updates).First(x=>ReferenceEquals(x.DataContext,error) && Equals(x.Content,LocalizationService.Translate("Riprova")));
        Check(retry.IsEnabled && retry.Visibility==Visibility.Visible,"Retry eligibility/localization broken");
        OperationsUiFixture.Set(vm,"IsBusy",true);
        Check(!retry.IsEnabled,"Retry ignores busy guard"); OperationsUiFixture.Set(vm,"IsBusy",false);

        Invoke(window,"ShowPage",Find<UIElement>(window,"HardwarePage"),"Driver e chipset"); window.UpdateLayout();
        Find<ComboBox>(window,"DriverTypeChoice").SetCurrentValue(Selector.SelectedValueProperty,"Con aggiornamenti");
        Check(Find<DataGrid>(window,"DriverInventoryGrid").Items.Cast<DriverInventoryItem>().All(x=>x.HasUpdate),"Driver category filter changed");
        Find<ComboBox>(window,"DriverTypeChoice").SetCurrentValue(Selector.SelectedValueProperty,"Tutti");
        var inventory = Find<DataGrid>(window,"DriverInventoryGrid");
        ((ScrollViewer)inventory.Template.FindName("DG_ScrollViewer", inventory)).ScrollToTop(); window.UpdateLayout();
        var repairButtons=Visuals<Button>(Find<DataGrid>(window,"DriverProblemsGrid")).Where(x=>x.DataContext is DriverProblemItem).ToArray();
        Check(repairButtons.Length==3,"Driver actions not realized");
        foreach(var button in repairButtons)
        {
            var problem=(DriverProblemItem)button.DataContext;
            Check(button.IsEnabled==problem.CanManageDriverProblem && Equals(button.Content,problem.RepairActionText),"Driver action eligibility changed");
            Check(button.ActualWidth>=100,"Driver repair action collapsed at narrow width");
        }
        JsonStorage.SaveHistory(vm.History);
        var history=JsonStorage.LoadHistory();
        Check(history.Count==500 && history[0].Diagnostics==vm.History[0].Diagnostics && history[0].FromVersion==vm.History[0].FromVersion,"History persistence changed");
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is T target) yield return target;
            foreach(var descendant in Visuals<T>(child)) yield return descendant;
        }
    }
    private static T Find<T>(MainWindow w,string name)=>(T)w.FindName(name);
    private static void Invoke(MainWindow w,string name,params object[] args)=>typeof(MainWindow).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(w,args);
    private static void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
}
