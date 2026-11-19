using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class ScanM7Tests
{
    internal static async Task RunAsync(bool native, bool cancellationOnly = false)
    {
        if (!native)
        {
            await RunHeadOnlyAsync();
            await ProcessCleanupAsync(false);
            await ProcessCleanupAsync(true);
            await ScanHardeningTests.RunAsync(); // Directly pertinent cancellation/error regressions.
            Console.WriteLine("PASS M7 offline: metadata HEAD only, process cancellation/timeout cleanup, scan error/cancellation states.");
            return;
        }
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try { await NativeAsync(cancellationOnly); done.SetResult(); }
                catch (Exception ex) { done.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromMinutes(15));
    }

    internal static async Task RunHeadOnlyAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try { await HeadOnlyAsync(); done.SetResult(); }
                catch (Exception ex) { done.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static async Task HeadOnlyAsync()
    {
        LocalizationService.Initialize("en");
        JsonStorage.SaveHistory([]);
        var handler = new HeadFixture();
        using var client = new HttpClient(handler);
        var installer = new Uri("https://example.invalid/fixture.exe");
        var cases = new (string Name, HttpStatusCode Status, long? Length, Exception? Error, long Expected)[]
        {
            ("missing Content-Length", HttpStatusCode.OK, null, null, 0),
            ("HEAD unsupported", HttpStatusCode.MethodNotAllowed, null, null, 0),
            ("server failure", HttpStatusCode.InternalServerError, 12345, null, 0),
            ("error-page length", HttpStatusCode.NotFound, 12345, null, 0),
            ("transport failure", HttpStatusCode.OK, null, new HttpRequestException("simulated transport failure"), 0),
            ("request timeout", HttpStatusCode.OK, null, new TaskCanceledException("simulated HTTP timeout"), 0),
            ("known size", HttpStatusCode.OK, 12345, null, 12345)
        };
        foreach (var test in cases)
        {
            JsonStorage.SaveSettings(new AppSettings());
            handler.Status = test.Status; handler.Length = test.Length; handler.Error = test.Error; handler.Requests.Clear();
            // Simulate verified package/manifest data; exercise the real size helper,
            // scan publication, size accounting and target validation. No provider executes.
            var item = new UpdateItem { Id = "Fixture.ValidTool", Name = "Fixture valid tool", Kind = UpdateKind.Software,
                Source = "winget", InstalledVersion = "1.0.0", AvailableVersion = "2.0.0" };
            var driversReached = false;
            var vm = new MainViewModel(new ScanServices(
                async (_, ct) =>
                {
                    item.DownloadSizeBytes = await WinGetManifestSafetyService.ReadDownloadSizeAsync(client, installer, ct);
                    return [item];
                },
                _ => Task.FromResult(new QuickHardwareSnapshot(new HardwareScanResult(), new StorageHealthScanResult())),
                _ => Task.FromResult(new HardwareScanResult()),
                (_, _) => { driversReached = true; return Task.FromResult(new DriverScanResult()); },
                (_, _) => Task.FromResult(new OfficialDriverCatalogScanResult()),
                _ => Task.FromResult<IReadOnlyList<GameDependencyItem>>([]),
                _ => Task.FromResult(new StorageHealthScanResult())));
            await vm.ScanAsync();
            Check(item.DownloadSizeBytes == test.Expected && handler.Requests.SequenceEqual([HttpMethod.Head]),
                test.Name + ": wrong size or an installer GET was requested.");
            Check(driversReached && vm.Progress == 100 && !vm.IsBusy && !vm.IsProgressIndeterminate &&
                !vm.ShowUpdatesActivity && vm.Settings.LastScanUtc is not null &&
                vm.Updates.Count == 1 && ReferenceEquals(vm.Updates[0], item),
                test.Name + ": size probe prevented scan completion or publication.");
            Check(item.CanInstall && item.IsSelected && vm.SelectedCount == 1 &&
                !item.RequiresRiskConfirmation && !item.HasUnverifiedInstallerMetadata,
                test.Name + ": optional size changed otherwise-valid eligibility.");
            var expectedCounts = test.Expected == 0 ? (0L, 0, 1) : (test.Expected, 1, 0);
            Check(PreflightService.CalculatePackageSize([item]) == expectedCounts,
                test.Name + ": unknown size was not accounted for separately from known sizes.");
            var snapshot = RunnerPlanSnapshot.Create(new UpdatePlan { Items = [ElevatedUpdateRunner.ToPlanItem(item)] }, false);
            Check(snapshot.Items.Count == 1 && RunnerPlanItem.Matches(item, snapshot.Items[0].ToPlanItem()),
                test.Name + ": valid exact target rejected or changed before consent.");
            Console.WriteLine("PASS M7 HEAD: " + test.Name + "; scan completed, eligibility and exact target preserved.");
        }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel(); handler.Error = null;
        try
        {
            await WinGetManifestSafetyService.ReadDownloadSizeAsync(client, installer, cancelled.Token);
            throw new InvalidOperationException("Caller cancellation was swallowed as unknown size.");
        }
        catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
        Console.WriteLine("PASS M7 HEAD: caller cancellation preserved. HTTP simulated; no GET or body read.");
    }

    private sealed class HeadFixture : HttpMessageHandler
    {
        internal HttpStatusCode Status;
        internal long? Length;
        internal Exception? Error;
        internal List<HttpMethod> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.Method);
            token.ThrowIfCancellationRequested();
            if (Error is not null) return Task.FromException<HttpResponseMessage>(Error);
            var response = new HttpResponseMessage(Status) { Content = new HeadersOnlyContent() };
            if (Length is long length) response.Content.Headers.ContentLength = length;
            Check(response.Content.Headers.ContentLength == Length, "Fixture did not preserve a missing Content-Length header.");
            return Task.FromResult(response);
        }
    }

    private sealed class HeadersOnlyContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("A size-only probe attempted to read a response body.");
    }

    private static async Task ProcessCleanupAsync(bool timeout)
    {
        var marker = Path.Combine(AppPaths.DataDirectory, "child-" + Guid.NewGuid() + ".pid");
        Directory.CreateDirectory(AppPaths.DataDirectory);
        using var cancel = new CancellationTokenSource();
        var child = ProcessRunner.RunAsync(Environment.ProcessPath!, ["--m7-wait-helper", marker], cancel.Token,
            timeout ? TimeSpan.FromSeconds(4) : TimeSpan.FromSeconds(20));
        var until = Stopwatch.StartNew();
        while (!File.Exists(marker) && !child.IsCompleted && until.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(50);
        Check(File.Exists(marker), "Controlled process did not reach its waiting state.");
        var pid = int.Parse(File.ReadAllText(marker));
        if (!timeout) cancel.Cancel();
        try { await child; throw new InvalidOperationException("Waiting process completed without cancellation/timeout."); }
        catch (OperationCanceledException) when (!timeout) { }
        catch (TimeoutException) when (timeout) { }
        await Task.Delay(500);
        Check(!Alive(pid), "Controlled process remains after cancellation/timeout.");
    }

    private static bool Alive(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static int[] WingetProcesses() => Process.GetProcessesByName("winget").Select(p => { using (p) return p.Id; }).Order().ToArray();

    private static int[] Descendants(IReadOnlyDictionary<int, NativeProcessSnapshot.Entry> processes, IEnumerable<int> roots)
    {
        var ids = roots.ToHashSet();
        while (true)
        {
            var children = processes.Values.Where(p => ids.Contains(p.ParentProcessId) && !ids.Contains(p.ProcessId)).Select(p => p.ProcessId).ToArray();
            if (children.Length == 0) break;
            ids.UnionWith(children);
        }
        return ids.Where(pid => pid != Environment.ProcessId && processes.ContainsKey(pid)).Order().ToArray();
    }

    private static async Task NativeAsync(bool cancellationOnly)
    {
        Environment.SetEnvironmentVariable("UPDATECENTER_SCAN_DIAGNOSTICS", "1");
        LocalizationService.Initialize("en");
        JsonStorage.SaveSettings(new AppSettings { ScanAtStartup = false, AutomaticScanInterval = "Off", CheckAppUpdatesAutomatically = false });
        JsonStorage.SaveHistory([]);
        var output = Path.GetFullPath("_output/engine-m7"); Directory.CreateDirectory(output);
        var path = Path.Combine(output, "native-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".jsonl");
        Console.WriteLine("M7 native read-only evidence: " + path);
        Console.WriteLine("OS=" + Environment.OSVersion.VersionString + "; runtime=" + Environment.Version + "; tiered=" + Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"));
        var vm = new MainViewModel(ScanServices.Create());
        foreach (var scenario in cancellationOnly ? new[] { "cancel-software" } : new[] { "complete-1", "complete-2", "cancel-software", "cancel-wua", "complete-3" })
        {
            var watch = Stopwatch.StartNew();
            var baseline = WingetProcesses();
            var gaps = new List<double>();
            var last = watch.Elapsed.TotalMilliseconds;
            double? phaseAt = null, requestedAt = null;
            int[] observedChildren = [];
            void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
            {
                if (args.PropertyName == nameof(vm.CurrentItemText) &&
                    ((scenario == "cancel-software" && vm.CurrentItemText == "Searching for software updates…") ||
                    (scenario == "cancel-wua" && vm.CurrentItemText == "Searching for driver updates…")))
                    phaseAt = watch.Elapsed.TotalMilliseconds;
            }
            vm.PropertyChanged += Changed;
            var probe = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(200) };
            probe.Tick += (_, _) =>
            {
                var now = watch.Elapsed.TotalMilliseconds; gaps.Add(now - last); last = now;
                if (phaseAt is not null && requestedAt is null && now - phaseAt >= 500)
                {
                    observedChildren = Descendants(NativeProcessSnapshot.Read(), [Environment.ProcessId]);
                    requestedAt = watch.Elapsed.TotalMilliseconds; vm.CancelScan();
                }
            };
            probe.Start();
            Console.WriteLine("START " + scenario);
            await vm.ScanAsync();
            var returned = watch.Elapsed.TotalMilliseconds;
            probe.Stop(); vm.PropertyChanged -= Changed;
            await Task.Delay(1500); // Observe process exit without including it in scan duration.
            var residual = WingetProcesses().Except(baseline).ToArray();
            var processSnapshot = NativeProcessSnapshot.Read();
            Check(processSnapshot.ContainsKey(Environment.ProcessId), "Process snapshot unavailable.");
            var residualChildren = Descendants(processSnapshot, observedChildren.Append(Environment.ProcessId));
            var reportLine = Directory.GetFiles(AppPaths.LogsDirectory, "*.log").SelectMany(File.ReadLines)
                .Last(x => x.Contains("operation=scan-baseline | phase=complete"));
            using var json = JsonDocument.Parse(reportLine.Split("details=", 2)[1]);
            var report = json.RootElement;
            var ordered = gaps.Order().ToArray();
            var evidence = new
            {
                Scenario = scenario, Report = report, DispatcherSamples = gaps.Count,
                DispatcherMaxGapMs = gaps.DefaultIfEmpty(0).Max(),
                DispatcherP95GapMs = ordered.Length == 0 ? 0 : ordered[(int)((ordered.Length - 1) * .95)],
                CancelReturnMs = requestedAt is null ? (double?)null : returned - requestedAt,
                ObservedChildrenBeforeCancellation = observedChildren.Length,
                ResidualChildrenAfter1500ms = residualChildren,
                NewWingetProcessesAfter1500ms = residual, UiBusyAfter = vm.IsBusy, IndeterminateAfter = vm.IsProgressIndeterminate
            };
            File.AppendAllText(path, JsonSerializer.Serialize(evidence) + Environment.NewLine);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                scenario, Outcome = report.GetProperty("Outcome").GetString(), TotalMs = report.GetProperty("TotalMs").GetDouble(),
                evidence.DispatcherMaxGapMs, evidence.CancelReturnMs, ResidualWinget = residual.Length,
                evidence.ObservedChildrenBeforeCancellation, ResidualChildren = residualChildren.Length,
                Phases = report.GetProperty("Phases").EnumerateArray().Where(p => p.GetProperty("Name").GetString()!.StartsWith("provider.") || p.GetProperty("Name").GetString() == "wait.wua-search")
                    .Select(p => new { Name = p.GetProperty("Name").GetString(), Ms = p.GetProperty("DurationMs").GetDouble() }).ToArray()
            }));
            Check(!vm.IsBusy && !vm.IsScanRunning && !vm.IsProgressIndeterminate, "Scan left UI busy.");
            Check(residual.Length == 0, "New WinGet process persists after scan.");
            Check(residualChildren.Length == 0, "Owned process remains after scan/cancellation.");
            if (scenario.StartsWith("cancel-")) Check(requestedAt is not null && report.GetProperty("Outcome").GetString() == "cancelled", "Cancellation was not exercised.");
        }
    }

    private static void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
}
