using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UpdateCenter;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;
using static DialogUiFixture;

internal static class DialogUiTests
{
    public static void Run(bool appOnly = false)
    {
        Exception? failure=null;
        var thread=new Thread(()=>{try{Verify(appOnly);}catch(Exception ex){failure=ex;}});
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure is not null)ExceptionDispatchInfo.Capture(failure).Throw();
        if (appOnly) { Console.WriteLine("PASS VerifyAppUpdate: localized light/dark updater, compact layout and inert error path."); return; }
        Console.WriteLine("PASS dialogs UI: IT/EN × light/dark, large text and 720x500; local/remote risk decisions, blocked preflight, Escape/Enter policy and focus return; exact operation items, pause/resume, hide/reopen, success/failure/cancelled/restart results, diagnostics, row recycling, app-update failure with rejecting transport; QA actions detached and LAN authorized/error/denied distinguishable. No installer, shutdown, real network request or UAC executed.");
    }

    private static void Verify(bool appOnly)
    {
        JsonStorage.SaveSettings(new AppSettings{ScanAtStartup=false,AutomaticScanInterval="Off",CheckAppUpdatesAutomatically=false,NotifyWhenUpdatesAreAvailable=false});
        // Do not instantiate App: its queued Startup would create a production MainWindow
        // when ShowDialog first pumps the dispatcher. Load only its resources instead.
        var app=new Application();
        var source=new DirectoryInfo(AppContext.BaseDirectory);
        while(source is not null && !File.Exists(Path.Combine(source.FullName,"App.xaml")))source=source.Parent;
        var applicationXaml=System.Xml.Linq.XDocument.Load(Path.Combine(source?.FullName??throw new InvalidOperationException("App.xaml not found"),"App.xaml")).Root!;
        System.Xml.Linq.XNamespace presentation="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var resources=new System.Xml.Linq.XElement(presentation+"ResourceDictionary",applicationXaml.Attributes().Where(a=>a.IsNamespaceDeclaration),applicationXaml.Element(presentation+"Application.Resources")!.Nodes());
        app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString());
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        TypographyService.Apply("Grande");
        var ownerButton=new Button{Content="QA owner"};
        var owner=new Window{Width=720,Height=500,Left=-20000,Top=-20000,WindowStartupLocation=WindowStartupLocation.Manual,ShowInTaskbar=false,Content=ownerButton};
        owner.Show();owner.UpdateLayout();
        try
        {
            foreach(var locale in new[]{"it","en"})
            foreach(var theme in new[]{"Chiaro","Scuro"})
            {
                LocalizationService.Initialize(locale);ThemeService.Apply(theme);
                if (appOnly)
                {
                    VerifyHomeAppUpdateNotice();
                    VerifyAppUpdate(owner);
                    continue;
                }
                VerifyConfirmation(owner,ownerButton);
                VerifyProgress(owner);
                VerifyAppUpdate(owner);
                var recovery=Create("recovery","warning");
                Modal(owner,recovery,w=>
                {
                    Layout(w);Check(!Find<Button>(w,"ConfirmButton").IsDefault,"Recovery gained an implicit confirm shortcut");
                    Escape(w);
                });
            }
            if (!appOnly) VerifyQaAndLan();
        }
        finally{owner.Close();app.Shutdown();}
    }

    private static void VerifyConfirmation(Window owner,Button ownerButton)
    {
        var items=Items(500);
        foreach(var blocked in new[]{false,true})
        {
            var confirmation=new UpdateConfirmationWindow(items,Preflight(blocked),true,true);
            owner.Activate();ownerButton.Focus();
            Console.WriteLine($"Focus before modal: active={owner.IsActive}; keyboard={Keyboard.FocusedElement}; logical={FocusManager.GetFocusedElement(owner)}");
            var result=Modal(owner,confirmation,w=>
            {
                Layout(w);
                var confirm=Find<Button>(w,"ConfirmButton");var risk=Find<CheckBox>(w,"RiskAcceptanceCheckBox");
                Check(!confirm.IsEnabled,"Risk update enabled before consent");
                Check(!confirm.IsDefault,"Confirmation gained a global Enter shortcut");
                risk.IsChecked=true;Check(confirm.IsEnabled==!blocked,"Risk checkbox bypassed preflight");
                risk.IsChecked=false;Check(!confirm.IsEnabled,"Risk consent cannot be revoked");
                var grid=Find<DataGrid>(w,"ItemsGrid");Check(grid.Items.Count==500,"Confirmation lost selected items");
                grid.ScrollIntoView(grid.Items[499]);w.UpdateLayout();
                Check(Visuals<DataGridRow>(grid).Count()<50,"Confirmation lost row virtualization");
                Check(grid.Columns.All(c=>c.MinWidth>=100),"Confirmation columns collapse");
                SendKey(w,Key.Enter);Check(w.IsVisible,"Unfocused Enter confirmed the operation");
                Escape(w);
            });
            Check(result==false,"Escape did not reject confirmation");
            Console.WriteLine($"Focus after modal: active={owner.IsActive}; keyboard={Keyboard.FocusedElement}; logical={FocusManager.GetFocusedElement(owner)}");
            Check(owner.IsActive && Keyboard.FocusedElement==ownerButton,"Modal focus did not return to its owner");
        }
        var accepted=new UpdateConfirmationWindow(items,Preflight(),true,true);
        Check(Modal(owner,accepted,w=>{Find<CheckBox>(w,"RiskAcceptanceCheckBox").IsChecked=true;Click(Find<Button>(w,"ConfirmButton"));})==true,"Explicit risk confirmation failed");
        var remote=(UpdateConfirmationWindow)Create("remote","risk");
        Check(Modal(owner,remote,w=>
        {
            Layout(w);Check(!Find<Button>(w,"ConfirmButton").IsEnabled,"Remote risk consent missing");
            Check(Find<Button>(w,"ExcludeRiskItemsButton").Visibility==Visibility.Visible,"Remote risk exclusion hidden");
            Click(Find<Button>(w,"ExcludeRiskItemsButton"));
        })==true && remote.ExcludeRiskyItems,"Remote exclusion decision changed");
    }

    private static void VerifyProgress(Window owner)
    {
        var vm=new MainViewModel();var pause=new UpdatePauseController();
        typeof(MainViewModel).GetField("_activePauseController",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(vm,pause);
        Set(vm,"IsBusy",true);Set(vm,"Progress",37d);Set(vm,"CurrentItemText",LocalizationService.Text("Download simulato","Simulated download"));
        var actualItems=Items(1);actualItems[0].IsSelected=false; // A retry need not match the page's checkboxes.
        var progress=new UpdateProgressWindow(vm,actualItems){Owner=owner};
        try
        {
            progress.Show();progress.UpdateLayout();Layout(progress);
            var rows=Find<DataGrid>(progress,"OperationItemsGrid").Items;
            Check(rows.Count==1 && rows[0].GetType().GetProperty("Name")!.GetValue(rows[0]) is string name &&
                name==actualItems[0].Name,"Progress does not show the actual operation input");
            var pauseButton=Find<Button>(progress,"PauseButton");Check(pauseButton.IsEnabled,"Existing pause guard lost");
            Click(pauseButton);Check(pause.IsPauseRequested && vm.IsUpdatePauseRequested && !vm.IsUpdatePaused,"Pause is no longer requested at the item boundary");
            Click(pauseButton);Check(!pause.IsPauseRequested && !vm.IsUpdatePauseRequested,"Resume failed");
            Escape(progress);Check(!progress.IsVisible && progress.IsOperationInProgress && vm.IsBusy,"Dismissal cancelled an active operation");
            progress.BringToFront();Check(progress.IsVisible && progress.IsOperationInProgress,"Hidden operation cannot be recalled");
            progress.ShowFailure(LocalizationService.Text("Autorizzazione negata","Authorization denied"),"QA failure details");
            Check(!progress.IsOperationInProgress && Find<Button>(progress,"CloseButton").IsVisible,"Failure cannot be dismissed");
            Check(Find<Button>(progress,"RestartButton").Visibility==Visibility.Collapsed,"Failure exposes a restart action");
            progress.Close();
        }
        finally{pause.Cleanup();if(progress.IsVisible){SetCanClose(progress);progress.Close();}}
        foreach(var state in new[]{"success","mixed","cancelled","restart"})
        {
            var window=new UpdateProgressWindow(new MainViewModel(),Items()){Owner=owner};
            window.Show();window.ShowCompleted(Result(state));window.UpdateLayout();Layout(window);
            var grid=Find<DataGrid>(window,"OperationItemsGrid");Check(grid.Items.Count==5,"Completed results missing");
            var statuses=grid.Items.Cast<object>().Select(x=>x.GetType().GetProperty("Status")!.GetValue(x)?.ToString()).ToArray();
            if(state=="cancelled")Check(statuses.All(x=>x==LocalizationService.Translate("Annullato")),"Cancelled result shown as success");
            if(state=="mixed")Check(statuses.Distinct().Count()==5,"Mixed result dispositions are indistinguishable");
            Check(Find<Button>(window,"RestartButton").Visibility==(state=="restart"?Visibility.Visible:Visibility.Collapsed),"Restart affordance changed");
            Check(Find<Button>(window,"PauseButton").Visibility==Visibility.Collapsed,"Completed session still offers pause");
            Check(!window.IsOperationInProgress,"Completion still marked running");
            if(state=="restart")
            {
                VisualQaSafety.Attach(window);
                var restart=Find<Button>(window,"RestartButton");
                Check(VisualQaSafety.ApplicationHandlers(restart,ButtonBase.ClickEvent).Count==0,"QA restart remains executable");
                Click(restart); // Only after verifying that the production restart handler was detached.
            }
            window.Close();
        }
    }

    private static void VerifyAppUpdate(Window owner)
    {
        var proposal = AppUpdate();
        var requests = new List<Uri>();
        var service = RejectingAppService(request => requests.Add(request.RequestUri!));
        var w = new AppUpdateWindow(proposal, service) { Owner = owner };
        w.Show();
        w.UpdateLayout();
        Layout(w);
        CaptureAppUpdate(w);
        Check(w.FindName("AppVersionHeadline") is TextBlock headline && headline.Text == proposal.AvailableVersionLabel &&
            w.FindName("AppVersionDetails") is TextBlock details && details.Text.Contains(proposal.InstalledVersionLabel) && details.Text.Contains("128 MB"),
            "The update must have one version headline with current version and size in secondary text.");
        Check(w.FindName("AppReleaseNotes") is TextBox notes && notes.Text.Contains(
            LocalizationService.Text("Correzione dimostrativa", "Example fix")) &&
            !notes.Text.Contains("##") && !notes.Text.Contains(LocalizationService.Text("Example fix", "Correzione dimostrativa")),
            "Only the selected locale's release notes must appear as readable text.");
        Call(w,"SetBusy",true);
        Check(!Find<Button>(w,"UpdateButton").IsEnabled && !Find<Button>(w,"CloseButton").IsEnabled && !Find<Button>(w,"LaterButton").IsEnabled,"Busy guards changed");
        Escape(w);Check(w.IsVisible,"Escape bypassed the app-update busy guard");
        w.Close();Check(w.IsVisible,"Window close bypassed the app-update busy guard");
        Call(w,"SetBusy",false);
        Click(Find<Button>(w,"UpdateButton")); // Injected HttpClient rejects every request before network/download/installer work.
        PumpUntil(()=>Find<Button>(w,"UpdateButton").IsEnabled);
        w.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Check(requests.SequenceEqual([proposal.Sha256Uri]), "The error must pass through the synthetic checksum request exactly once.");
        Check(Find<UIElement>(w,"ErrorDetailsPanel").Visibility==Visibility.Visible,"Failed update has no technical details");
        Check(Find<TextBox>(w,"ErrorDetailsText").Text.Contains("QA transport"),"Technical error details empty");
        Check(Find<TextBox>(w,"DownloadStatus").Text == LocalizationService.Text(
            "Aggiornamento non applicato. Puoi riprovare.", "Update not applied. You can try again."), "App error not localized");
        PumpUntil(()=>Find<TextBox>(w,"DownloadStatus").TransformToAncestor(w).Transform(new Point()).Y < Find<WrapPanel>(w,"DialogActions").TransformToAncestor(w).Transform(new Point()).Y);
        VisualQaSafety.Attach(w);
        var update=Find<Button>(w,"UpdateButton");Check(VisualQaSafety.ApplicationHandlers(update,ButtonBase.ClickEvent).Count==0,"QA self-update remains executable");
        Click(update);Check(update.IsEnabled,"QA self-update handler executed after isolation");w.Close();
        foreach (var buttonName in new[] { "LaterButton", "CloseButton", "IgnoreButton" })
        {
            var dismiss = new AppUpdateWindow(proposal, service) { Owner = owner };
            dismiss.Show();
            Click(Find<Button>(dismiss, buttonName));
            Check(!dismiss.IsVisible && dismiss.IgnoreRequested == (buttonName == "IgnoreButton"),
                "Only the explicit skip action must mark the offer as ignored.");
        }
        Check(requests.Count == 1, "Dismissing the offer must not contact the release service.");
    }

    private static void VerifyHomeAppUpdateNotice()
    {
        var settings = JsonStorage.LoadSettings();
        settings.PendingAppUpdateVersion = "2.0.2";
        settings.LanguageMode = LocalizationService.CurrentLanguage;
        JsonStorage.SaveSettings(settings);
        var window = new MainWindow
        {
            Width = 720, Height = 500, Left = -20000, Top = -20000,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false
        };
        VisualQaSafety.Attach(window);
        window.Show();
        try
        {
            Call(window, "ShowPage", window.FindName("HomePage"), "Home");
            window.UpdateLayout();
            var notice = Find<Border>(window, "HomeAppUpdateNotice");
            var action = Find<Button>(window, "HomeAppUpdateButton");
            Check(notice.IsVisible && action.IsEnabled && action.ActualWidth > 0 &&
                Equals(action.Content, LocalizationService.Text("Vedi novità", "See what’s new")),
                "The saved version must be discoverable on Home with a localized action in the compact window.");
            Check(VisualQaSafety.ApplicationHandlers(action, ButtonBase.ClickEvent).Count == 0,
                "The preview must not dispatch a real update check.");
        }
        finally
        {
            window.Close();
            settings.PendingAppUpdateVersion = "";
            JsonStorage.SaveSettings(settings);
        }
    }

    private static void CaptureAppUpdate(Window window)
    {
        var state = window.FindName("AppVersionHeadline") is null ? "before" : "after";
        var palette = ThemeService.IsDark ? "dark" : "light";
        var directory = Path.GetFullPath("_output/v201-app-update/preview");
        Directory.CreateDirectory(directory);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render((Visual)window.Content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, $"app-update-{state}-{LocalizationService.CurrentLanguage}-{palette}.png"));
        encoder.Save(file);
    }

    private static void VerifyQaAndLan()
    {
        var w=new MainWindow{Left=-20000,Top=-20000,WindowStartupLocation=WindowStartupLocation.Manual};
        VisualQaSafety.Attach(w);var vm=(MainViewModel)w.DataContext;
        HardwareNetworkUiFixture.Populate(vm,"populated",50);
        var authorized=vm.Network.Agents[0].AssociationText;var error=vm.Network.Agents[2].ConnectionState;
        w.Show();Call(w,"ShowPage",w.FindName("NetworkPage"),"Gestione rete");VisualQaSafety.StopTimers(w);w.UpdateLayout();
        var button=Find<Button>(w,"NetworkUpdateSelectedButton");
        Check(button.IsEnabled==vm.Network.CanUpdateSelected,"QA changed the real eligibility condition");
        Check(VisualQaSafety.ApplicationHandlers(button,ButtonBase.ClickEvent).Count==0 && button.Command is null,"QA remote-update action still connected");
        var status=vm.Network.StatusText;Click(button);Check(!vm.Network.IsBusy && vm.Network.StatusText==status,"QA action changed remote execution state");
        HardwareNetworkUiFixture.Populate(vm,"denied",50);w.UpdateLayout();
        Check(!button.IsEnabled,"Authorization denied enables update");
        Check(vm.Network.Agents[0].AssociationText!=authorized && vm.Network.Agents[0].ConnectionState!=error,"LAN authorization/error/denial indistinguishable");
        foreach(var action in VisualQaSafety.Nodes(w).OfType<Button>())
            Check(VisualQaSafety.ApplicationHandlers(action,ButtonBase.ClickEvent).Count==0 && action.Command is null,"A QA button can still invoke production code");
        w.Close();
    }

    private static bool? Modal(Window owner,Window w,Action<Window> action)
    {
        Exception? failure=null;w.Owner=owner;
        w.Loaded+=(_,_)=>w.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
        {
            try{action(w);}catch(Exception ex){failure=ex;}finally{if(w.IsVisible)w.Close();}
        }));
        var result=w.ShowDialog();if(failure is not null)ExceptionDispatchInfo.Capture(failure).Throw();return result;
    }
    private static void Layout(Window w)
    {
        w.UpdateLayout();Check(w.ActualWidth<=720.5 && w.ActualHeight<=500.5,"Dialog exceeds the minimum owner window");
        var actions=Find<WrapPanel>(w,"DialogActions");
        foreach(var button in actions.Children.OfType<Button>().Where(x=>x.IsVisible))
        {
            var bounds=button.TransformToAncestor(w).TransformBounds(new Rect(button.RenderSize));
            Check(bounds.Left>=0 && bounds.Right<=w.ActualWidth+1 && bounds.Bottom<=w.ActualHeight+1,"Sensitive footer action clipped");
            Check(button.FocusVisualStyle is not null,"Action lacks keyboard focus");
        }
        Check(Visuals<ScrollViewer>(w).Any(x=>x.ViewportHeight>40),"Dialog body not reachable");
    }
    private static void PumpUntil(Func<bool> done)
    {
        var deadline=DateTime.UtcNow.AddSeconds(5);
        while(!done() && DateTime.UtcNow<deadline)
        {var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
        Check(done(),"Asynchronous simulated failure did not settle");
    }
    private static void SendKey(UIElement target,Key key)=>target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(target),0,key){RoutedEvent=Keyboard.PreviewKeyDownEvent});
    private static void Escape(Window w)=>SendKey(w,System.Windows.Input.Key.Escape);
    private static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void SetCanClose(UpdateProgressWindow w)=>typeof(UpdateProgressWindow).GetField("_canClose",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(w,true);
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T:DependencyObject
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var c=VisualTreeHelper.GetChild(root,i);if(c is T t)yield return t;foreach(var child in Visuals<T>(c))yield return child;}}
}
