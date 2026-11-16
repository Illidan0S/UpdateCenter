using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UpdateCenter;
using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class UnifiedConsentUiTests
{
    internal static void Run()
    {
        Exception? error = null;
        var thread = new Thread(() => { try { Verify(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error is not null) throw new Exception("Unified consent WPF QA", error);
    }

    private static void Verify()
    {
        JsonStorage.SaveSettings(new AppSettings { ScanAtStartup = false, CheckAppUpdatesAutomatically = false });
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var source = new DirectoryInfo(AppContext.BaseDirectory);
        while (source is not null && !File.Exists(Path.Combine(source.FullName, "App.xaml"))) source = source.Parent;
        var root = System.Xml.Linq.XDocument.Load(Path.Combine(source!.FullName, "App.xaml")).Root!;
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(new System.Xml.Linq.XElement(ns + "ResourceDictionary",
            root.Attributes().Where(x => x.IsNamespaceDeclaration), root.Element(ns + "Application.Resources")!.Nodes()).ToString());
        var output = Path.Combine(source.FullName, "_output", "simple-summary-20261109", "qa");
        Directory.CreateDirectory(output);
        foreach (var language in new[] { "it", "en" })
        foreach (var theme in new[] { "Chiaro", "Scuro" })
        {
            LocalizationService.Initialize(language); ThemeService.Apply(theme); TypographyService.Apply("Media");
            foreach (var scenario in new[] { "ordinary", "many", "risk" })
            {
                var items = Enumerable.Range(0, scenario == "many" ? 500 : 1).Select(i => Item(i, scenario == "risk")).ToArray();
                var plan = Plan(items);
                var hints = items.Select(item => new InstallationSummaryHint(item)).ToArray();
                if (scenario == "risk") { items[0].RequiresRiskConfirmation = false; items[0].WinGetBinding = null; }
                var window = new UpdateConfirmationWindow([plan], Preflight(), hints) { Width = 720, Height = scenario == "many" ? 500 : 600 };
                var gate = new UnifiedConsentGate([plan]); var progress = 0;
                var accepted = gate.Confirm(_ => Modal(window, w =>
                {
                    Check(w.DialogResult is null, "Opening a window authorized the plan");
                    Check(!VisualQaSafety.Nodes(w).OfType<CheckBox>().Any(), "Checkbox remains in local summary");
                    var confirm = Find<Button>(w, "ConfirmButton");
                    Check(confirm.IsEnabled && !confirm.IsDefault, "Direct confirmation disabled or global Enter enabled");
                    Click(Find<Button>(w, "ExcludeRiskItemsButton"));
                    Check(w.DialogResult is null, "Detached legacy handler authorized the local plan");
                    Find<CheckBox>(w, "RiskAcceptanceCheckBox").IsChecked = true;
                    Find<CheckBox>(w, "RiskAcceptanceCheckBox").IsChecked = false;
                    Check(confirm.IsEnabled, "Detached checkbox still controls authoritative consent");
                    var grid = Find<DataGrid>(w, "ItemsGrid");
                    Check(grid.Items.Count == items.Length && grid.Columns.Count == 3, "Readable list lost targets or versions");
                    var row = (InstallationSummaryRow)grid.Items[0];
                    Check(row.Name == "7-Zip" && row.InstalledVersion == "24.09" && row.AvailableVersion == "25.01", "Displayed target differs from snapshot");
                    Check(row.HasWarning == (scenario == "risk"), "Generic risk shown for ordinary update or concrete risk lost");
                    if (scenario == "risk") Check(row.Warning.Contains(LocalizationService.Text("Riavvio necessario", "Restart required")) &&
                        row.Warning.Contains(LocalizationService.Text("rimossa", "removed")), "Removal/restart warning missing");
                    var bounds = confirm.TransformToAncestor(w).TransformBounds(new Rect(confirm.RenderSize));
                    Check(bounds.Bottom <= w.ActualHeight && bounds.Right <= w.ActualWidth, "Confirm button clipped");
                    var visibleText = string.Join(" ", VisualQaSafety.Nodes(w).OfType<TextBlock>().Select(text => text.Text));
                    Check(!visibleText.Contains("S-1-5-") && !visibleText.Contains("ARP\\") && !visibleText.Contains("Fallback WinGet") &&
                        !visibleText.Contains("Conferma finale del piano ricevuto"), "Raw technical consent text is still visible");
                    if (scenario == "many")
                    {
                        grid.ScrollIntoView(grid.Items[499]); w.UpdateLayout();
                        Check(VisualQaSafety.Nodes(grid).OfType<DataGridRow>().Count() < 50, "Large selection lost virtualization");
                        grid.ScrollIntoView(grid.Items[0]); w.UpdateLayout();
                    }
                    Save(w, Path.Combine(output, $"summary-{language}-{theme}-{scenario}.png"));
                    Click(confirm); Click(confirm); // A repeated click must not create a second decision.
                }) == true, () => progress++, () => true);
                Check(accepted && progress == 1, "One explicit decision did not open progress exactly once");
                gate.Consume(0, plan);
                ExpectBlocked(() => gate.Consume(0, plan));
                Console.WriteLine($"PASS WPF {language}/{theme}/{scenario}: direct click, exact rows, no checkbox/raw text, one-use and fixed footer");
            }
        }
        LocalizationService.Initialize("it");
        var ordinary = Item(0, false); var snapshot = Plan([ordinary]);
        foreach (var action in new[] { "cancel", "escape", "close", "x" })
        {
            var w = new UpdateConfirmationWindow([snapshot], Preflight());
            var gate = new UnifiedConsentGate([snapshot]); var effects = 0;
            Check(!gate.Confirm(_ => Modal(w, window =>
            {
                if (action == "escape") Key(window, System.Windows.Input.Key.Escape, Keyboard.PreviewKeyDownEvent);
                else if (action == "cancel") Click(Find<Button>(window, "CancelButton"));
                else if (action == "x") Click(VisualQaSafety.Nodes(window).OfType<Button>().Single(button => Equals(button.Content, "×")));
                else window.Close();
            }) == true, () => effects++, () => true), "Dismissal authorized effects: " + action);
            Check(effects == 0, "Dismissal opened progress");
            ExpectBlocked(() => gate.Consume(0, snapshot));
            Console.WriteLine("PASS denial through " + action);
        }
        foreach (var key in new[] { System.Windows.Input.Key.Enter, System.Windows.Input.Key.Space })
        {
            var w = new UpdateConfirmationWindow([snapshot], Preflight());
            Check(Modal(w, window =>
            {
                var confirm = Find<Button>(window, "ConfirmButton");
                Key(window, System.Windows.Input.Key.Enter, Keyboard.KeyDownEvent);
                Check(window.DialogResult is null, "Unfocused Enter authorized installation");
                confirm.Focus();
                Key(confirm, key, Keyboard.KeyDownEvent);
                if (key == System.Windows.Input.Key.Space) Key(confirm, key, Keyboard.KeyUpEvent);
            }) == true, "Focused keyboard activation did not confirm: " + key);
            Console.WriteLine("PASS explicit keyboard activation " + key);
        }
        var blocked = Preflight(); blocked.BlockingIssues.Add("Synthetic preflight block");
        Check(Modal(new UpdateConfirmationWindow([snapshot], blocked), w =>
        {
            Check(!Find<Button>(w, "ConfirmButton").IsEnabled, "Preflight bypassed");
            Click(Find<Button>(w, "ConfirmButton")); Check(w.DialogResult is null, "Disabled click authorized effects"); w.Close();
        }) != true, "Blocked preflight authorized plan");
        var hint = new InstallationSummaryHint(ordinary);
        var changed = UnifiedConsentGate.ToPlan(snapshot); changed.Items[0].AvailableVersion = "99.0";
        ExpectBlocked(() => new UpdateConfirmationWindow([RunnerPlanSnapshot.Create(changed, false)], Preflight(), [hint]));
        Console.WriteLine("PASS blocked preflight and presentation hints bound to exact snapshot");
        var driverId = Guid.NewGuid().ToString();
        var driver = new UpdateItem { Id = driverId, Name = "Synthetic device", Kind = UpdateKind.Driver, Source = "Windows Update",
            InstalledVersion = "1.0", AvailableVersion = "2.0", WindowsUpdateId = driverId, WindowsUpdateRevision = 1,
            DriverInstallMode = DriverInstallModes.WindowsUpdate, WindowsUpdateServerSelection = 2, RequiresRestart = true };
        var driverPlan = RunnerPlanSnapshot.Create(new UpdatePlan { CreateRestorePoint = true,
            Items = [ElevatedUpdateRunner.ToPlanItem(driver)] }, true);
        Check(Modal(new UpdateConfirmationWindow([snapshot, driverPlan], Preflight(),
            [new InstallationSummaryHint(ordinary), new InstallationSummaryHint(driver)]), w =>
        {
            var rows = Find<DataGrid>(w, "ItemsGrid").Items.Cast<InstallationSummaryRow>().ToArray();
            Check(rows.Length == 2 && rows[1].Warning.Contains("Riavvio necessario") &&
                Find<TextBlock>(w, "DriverCountText").Text == "1" && Find<Border>(w, "RestorePointPanel").IsVisible,
                "Mixed summary lost driver restart warning or system protection");
            Click(Find<Button>(w, "ConfirmButton"));
        }) == true, "Mixed summary could not confirm directly");
        Console.WriteLine("PASS mixed software/driver summary with restore point and specific restart warning");
        TypographyService.Apply("Grande");
        foreach (var width in new[] { 480d, 720d })
        {
            var small = new UpdateConfirmationWindow([snapshot], Preflight()) { Width = width, Height = 500 };
            Modal(small, w =>
            {
                var button = Find<Button>(w, "ConfirmButton");
                var bounds = button.TransformToAncestor(w).TransformBounds(new Rect(button.RenderSize));
                Check(bounds.Right <= w.ActualWidth && bounds.Bottom <= w.ActualHeight && button.IsEnabled,
                    "Large-text compact layout hides the confirmation action");
                w.Close();
            });
        }
        Console.WriteLine("PASS large text at 480x500 and 720x500; screen settings unchanged");
        app.Shutdown();
    }

    private static UpdateItem Item(int index, bool risk) => new()
    {
        Id = index == 0 ? "7zip.7zip" : "Contoso.Tool" + index, Name = index == 0 ? "7-Zip" : "Synthetic app " + index,
        Kind = UpdateKind.Software, Source = "winget", InstalledVersion = "24.09", AvailableVersion = "25.01",
        RequiresRestart = risk, RequiresRiskConfirmation = risk,
        CompatibilityDetail = risk ? LocalizationService.Text("Incompatibilità nota con il componente dimostrativo.", "Known incompatibility with the demo component.") : "",
        WinGetBinding = risk ? new("user", "ARP\\User\\X64\\Tool", "S-1-5-21-1000", "v1.29.290", new string('A', 64), "inno", "x64", "RemovesPreviousVersion") : null
    };
    private static RunnerPlanSnapshot Plan(IReadOnlyList<UpdateItem> items) => RunnerPlanSnapshot.Create(
        new UpdatePlan { Items = items.Select(ElevatedUpdateRunner.ToPlanItem).ToList() }, false);
    private static PreflightResult Preflight() => new() { PowerStatus = "Alimentatore collegato", DiskStatus = "100 GB" };
    private static T Find<T>(Window w, string name) => (T)w.FindName(name);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void Key(UIElement target, Key key, RoutedEvent routedEvent) => target.RaiseEvent(
        new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key) { RoutedEvent = routedEvent });
    private static bool? Modal(Window window, Action<Window> action)
    {
        Exception? error = null;
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try { window.UpdateLayout(); action(window); }
            catch (Exception ex) { error = ex; window.Close(); }
            finally { if (window.IsVisible) window.Close(); }
        }));
        var result = window.ShowDialog();
        if (error is not null) throw error;
        return result;
    }
    private static void Save(Window w, string path)
    {
        w.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(w);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Check(bool value, string message) => UnifiedConsentTests.Check(value, message);
    private static void ExpectBlocked(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Unconfirmed or mismatched plan was accepted");
    }
}
