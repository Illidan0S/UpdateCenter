using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class WindowsUpdateCoverageTests
{
    private const string Switch = "UPDATECENTER_SCAN_DIAGNOSTICS";
    private const string Secret = "PRIVATE-HARDWARE-ENDPOINT-TOKEN";

    internal static void Run(bool measure)
    {
        var previous = Environment.GetEnvironmentVariable(Switch);
        try
        {
            Check(!string.Equals(Path.GetFullPath(AppPaths.DataDirectory), Path.GetFullPath(AppPaths.ResolveDefaultRoot()),
                StringComparison.OrdinalIgnoreCase), "Production data root selected");
            Console.WriteLine($"Isolated data root: {AppPaths.DataDirectory}");
            if (measure) { Measure(); return; }
            Environment.SetEnvironmentVariable(Switch, "1");
            var failures = new List<string>();
            foreach (var test in new Action[] { ZeroOffers, ExclusionReasons, AllowedClassesAndOptional,
                PartialAndWarningFallback, FailedResults, MetadataFailure, DuplicatesAndSources,
                DiscoveryFailure, UnreadableCollection, Cancellation, DisabledDiagnostics, WindowsUpdateSafetyTests.Run })
            {
                try { test(); Console.WriteLine($"PASS {test.Method.Name}"); }
                catch (Exception ex) { failures.Add(test.Method.Name + ": " + ex.Message); Console.WriteLine("FAIL " + failures[^1]); }
            }
            if (failures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
            Console.WriteLine("PASS WUA coverage: isolated fixtures; no real COM, downloads or installations.");
        }
        finally { Environment.SetEnvironmentVariable(Switch, previous); }
    }

    private static void ZeroOffers()
    {
        var env = new CoverageWuaEnvironment();
        var capture = Capture(env);
        Check(capture.Error is null && capture.Result!.Updates.Count == 0 && capture.Result.SourceWarnings.Count == 0,
            "Empty successful source must not become an error");
        Check(env.Queried.SequenceEqual(new[] { 0, 2 }), "Empty configured source did not trigger online fallback");
        Count(capture, "wua.sources-queried", 2);
        Count(capture, "wua.configured.no-offers", 1);
        Count(capture, "wua.online.no-offers", 1);
        Outcome(capture, "success");
    }

    private static void ExclusionReasons()
    {
        var rejected = new List<FakeDriverOffer>();
        foreach (var name in new[] { "SecurityDevices", "System", "Processor", "Extension", "SoftwareComponent" })
            rejected.Add(new() { DriverClass = name });
        rejected.Add(new() { DriverClass = "Firmware" });
        rejected.Add(new() { Title = "Display BIOS 2.0" });
        rejected.Add(new() { DriverHardwareID = @"ACPI\UEFI_RES_1234" });
        rejected.Add(new() { DriverHardwareID = "" });
        rejected.Add(new() { Title = "" });
        var badIdentity = new FakeDriverOffer();
        badIdentity.Identity.UpdateID = "invalid";
        rejected.Add(badIdentity);
        rejected.Add(new() { DriverClass = Secret });
        var env = new CoverageWuaEnvironment { Configured = new(2, rejected.ToArray()) };
        var capture = Capture(env);
        Check(capture.Error is null && capture.Result!.Updates.Count == 0, "Rejected candidates escaped");
        Count(capture, "wua.configured.candidates", 12);
        Count(capture, "wua.configured.examined", 12);
        Count(capture, "wua.configured.excluded", 12);
        Count(capture, "wua.configured.excluded.class-not-allowed", 6);
        Count(capture, "wua.configured.excluded.firmware", 3);
        Count(capture, "wua.configured.excluded.invalid-identity", 2);
        Count(capture, "wua.configured.excluded.incomplete-metadata", 1);
        foreach (var name in new[] { "securitydevices", "system", "processor", "extension", "softwarecomponent", "other" })
            Count(capture, "wua.configured.rejected-class." + name, 1);
        Count(capture, "wua.configured.all-excluded", 1);
        Check(!Logs().Contains(Secret, StringComparison.Ordinal), "Raw class/identity leaked into logs");
        Outcome(capture, "success");
    }

    private static void AllowedClassesAndOptional()
    {
        foreach (var name in new[] { "Display", "Display adapters", "Schede video", "Net", "Network adapters", "Schede di rete",
            "Media", "AudioEndpoint", "AudioProcessingObject", "Bluetooth", "Camera", "Image", "HIDClass", "Keyboard", "Mouse",
            "Monitor", "Printer", "PrintQueue", "Biometric", "HDC", "SCSIAdapter", "DiskDrive", "CDROM", "USB", "USBDevice", "Sensor", "Ports" })
        {
            var driver = new FakeDriverOffer { DriverClass = name, BrowseOnly = true, AutoSelectOnWebSites = false };
            var capture = Capture(new() { Configured = new(2, driver) });
            Check(capture.Error is null && capture.Result!.Updates.Count == 1, "Allowed class lost: " + name);
            Check(capture.Result!.Updates[0].IsOptional && !capture.Result.Updates[0].IsSelected, "Optional flags changed");
            Count(capture, "wua.eligible", 1);
            Count(capture, "wua.admitted", 1);
            Count(capture, "wua.online.skipped", 1);
            Outcome(capture, "success");
        }
    }

    private static void PartialAndWarningFallback()
    {
        foreach (var result in new[] { new CoverageWuaResult(3, Offer(1)),
            new CoverageWuaResult(2, Offer(1)) { WarningCount = 2 },
            new CoverageWuaResult(2, Offer(1)) { UnreadableWarnings = true } })
        {
            var env = new CoverageWuaEnvironment { Configured = result, Online = new(2, Offer(2)) };
            var capture = Capture(env);
            Check(capture.Error is null && capture.Result!.Updates.Count == 2 && capture.Result.SourceWarnings.Count > 0,
                "Partial/warning source was treated as complete or its safe candidates were lost");
            Check(env.Queried.SequenceEqual(new[] { 0, 2 }), "Partial configured source suppressed fallback");
            Count(capture, "wua.configured.partial", 1);
            Outcome(capture, "partial");
        }
        var empty = Capture(new() { Configured = new(3), Online = new(2) });
        Check(empty.Result!.Updates.Count == 0 && empty.Result.SourceWarnings.Count > 0, "Partial zero became complete zero");
        Outcome(empty, "partial");
    }

    private static void FailedResults()
    {
        foreach (var code in new[] { 0, 1, 4, 5, 99 })
        {
            var capture = Capture(new() { Configured = new(code, Offer(1)), Online = new(2, Offer(2)) });
            Check(capture.Error is null && capture.Result!.Updates.Count == 1 && capture.Result.Updates[0].WindowsUpdateId == Offer(2).Identity.UpdateID,
                "Failed/unexpected result admitted candidates: " + code);
            Check(capture.Result!.SourceWarnings.Count > 0, "Failed result omitted warning");
            Count(capture, "wua.configured.search-errors", 1);
            Outcome(capture, "partial");
        }
        var unreadable = Capture(new() { Configured = new(2, Offer(1)) { UnreadableStatus = true }, Online = new(2) });
        Check(unreadable.Result!.Updates.Count == 0 && unreadable.Result.SourceWarnings.Count > 0, "Unreadable status accepted");
        var allFailed = Capture(new() { Configured = new(4), Online = new(5) });
        Check(allFailed.Error is InvalidOperationException && allFailed.Result is null, "All failed sources returned successful empty scan");
        Outcome(allFailed, "error");
        var com = Capture(new() { SearchError = new COMException(Secret, unchecked((int)0x8024001E)), Online = new(2, Offer(2)) });
        Check(com.Result!.Updates.Count == 1 && com.Result.SourceWarnings.Count > 0, "COM fallback lost");
        Check(!Logs().Contains(Secret, StringComparison.Ordinal), "Exception metadata leaked into ordinary logs");
    }

    private static void MetadataFailure()
    {
        var broken = new FakeDriverOffer { DescriptionReadable = false };
        var env = new CoverageWuaEnvironment { Configured = new(2, broken, Offer(2)) };
        var capture = Capture(env);
        Check(capture.Result!.Updates.Count == 1 && capture.Result.SourceWarnings.Count > 0, "Metadata failure not reported as partial");
        Count(capture, "wua.configured.excluded.metadata-read-error", 1);
        Outcome(capture, "partial");
        var optional = new FakeDriverOffer { VersionDateReadable = false };
        capture = Capture(new() { Configured = new(2, optional) });
        Check(capture.Error is null && capture.Result!.Updates.Count == 1 && capture.Result.SourceWarnings.Count > 0,
            "Optional metadata read failure removed offer or became complete success");
        Count(capture, "wua.configured.metadata-read-errors", 1);
        Outcome(capture, "partial");
        env = new() { Configured = new(2, Offer(1), Offer(2)) { BadItem = 0 } };
        capture = Capture(env);
        Check(capture.Error is null && capture.Result!.Updates.Count == 1, "Unreadable item discarded subsequent candidates");
        Count(capture, "wua.configured.examined", 2);
        Count(capture, "wua.configured.excluded.metadata-read-error", 1);
        Outcome(capture, "partial");
    }

    private static void DuplicatesAndSources()
    {
        var env = new CoverageWuaEnvironment
        {
            HasMicrosoft = true, Configured = new(2, Offer(1), Offer(1)), Microsoft = new(2, Offer(1), Offer(2))
        };
        var capture = Capture(env);
        Check(capture.Result!.Updates.Count == 2 && capture.Result.SourcesChecked.Count == 2, "Deduplication/sources changed");
        Check(env.Queried.SequenceEqual(new[] { 0, 3 }), "Registered Microsoft source missed or unnecessary fallback");
        Count(capture, "wua.candidates", 4);
        Count(capture, "wua.examined", 4);
        Count(capture, "wua.eligible", 4);
        Count(capture, "wua.duplicates", 2);
        Count(capture, "wua.admitted", 2);
        Count(capture, "wua.returned", 2);
        Count(capture, "wua.configured.duplicates", 1);
        Count(capture, "wua.microsoft.duplicates", 1);
        Outcome(capture, "success");
    }

    private static void DiscoveryFailure()
    {
        var capture = Capture(new() { DiscoveryError = true });
        Check(capture.Error is null && capture.Result!.SourceWarnings.Count > 0, "Service discovery failure hidden");
        Count(capture, "wua.discovery-errors", 1);
        Outcome(capture, "partial");
    }

    private static void UnreadableCollection()
    {
        var capture = Capture(new() { Configured = new(2) { NullUpdates = true }, Online = new(2, Offer(2)) });
        Check(capture.Error is null && capture.Result!.Updates.Count == 1 && capture.Result.SourceWarnings.Count > 0,
            "Unreadable Updates collection prevented healthy fallback");
        Count(capture, "wua.configured.collection-read-errors", 1);
        Outcome(capture, "partial");
    }

    private static void Cancellation()
    {
        using var before = new CancellationTokenSource();
        before.Cancel();
        var env = new CoverageWuaEnvironment();
        var capture = Capture(env, before.Token);
        Check(capture.Error is OperationCanceledException && env.Queried.Count == 0, "Pre-cancel still queried sources");
        using var during = new CancellationTokenSource();
        env = new() { Configured = new(2, Offer(1)), AfterSearch = during.Cancel };
        capture = Capture(env, during.Token);
        Check(capture.Error is OperationCanceledException && env.Queried.Count == 1 && capture.Result is null,
            "Cancellation after synchronous Search admitted result or continued fallback");
        Outcome(capture, "cancelled");
        using var betweenItems = new CancellationTokenSource();
        env = new() { Configured = new(2, Offer(1), Offer(2)) { ItemRead = i => { if (i == 1) betweenItems.Cancel(); } } };
        capture = Capture(env, betweenItems.Token);
        Check(capture.Error is OperationCanceledException && capture.Result is null, "Mid-collection cancellation lost");
        Count(capture, "wua.examined", 2);
        Count(capture, "wua.eligible", 1);
        Outcome(capture, "cancelled");
    }

    private static void DisabledDiagnostics()
    {
        Environment.SetEnvironmentVariable(Switch, null);
        try
        {
            var before = Reports().Count;
            using var scan = ScanDiagnostics.Start();
            var result = WindowsUpdateService.ScanDrivers(default, [], new CoverageWuaEnvironment { Configured = new(2, Offer(1)) }.Create);
            Check(scan is null && result.Updates.Count == 1 && Reports().Count == before, "Disabled diagnostics emitted report or changed scan");
        }
        finally { Environment.SetEnvironmentVariable(Switch, "1"); }
    }

    private static void Measure()
    {
        var offers = Enumerable.Range(1, 250).Select(i => { var d = Offer(i); if (i % 5 == 0) d.DriverClass = "System"; return d; }).ToArray();
        var env = new CoverageWuaEnvironment { Configured = new(2, offers) };
        for (var sample = -3; sample < 20; sample++)
        foreach (var enabled in sample % 2 == 0 ? new[] { false, true } : new[] { true, false })
        {
            Environment.SetEnvironmentVariable(Switch, enabled ? "1" : null);
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            using (var scan = ScanDiagnostics.Start())
            {
                var result = WindowsUpdateService.ScanDrivers(default, [], env.Create);
                Check(result.Updates.Count == 200, "Measurement fixture result changed");
                scan?.Complete("success");
            }
            var ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            if (sample >= 0) Console.WriteLine("MEASURE " + JsonSerializer.Serialize(new { sample, enabled, ms, bytes }));
        }
    }

    private static FakeDriverOffer Offer(int id)
    {
        var offer = new FakeDriverOffer();
        offer.Identity.UpdateID = new Guid(id, 0, 0, new byte[8]).ToString();
        return offer;
    }

    private sealed record Captured(DriverScanResult? Result, Exception? Error, JsonElement Report);
    private static Captured Capture(CoverageWuaEnvironment env, CancellationToken token = default)
    {
        DriverScanResult? result = null;
        Exception? error = null;
        using (var scan = ScanDiagnostics.Start())
        {
            try { result = WindowsUpdateService.ScanDrivers(token, [], env.Create); scan!.Complete("success"); }
            catch (Exception ex) { error = ex; scan!.Complete(ex is OperationCanceledException ? "cancelled" : "error"); }
        }
        return new(result, error, Reports()[^1]);
    }
    private static string Logs() => Directory.Exists(AppPaths.LogsDirectory)
        ? string.Join('\n', Directory.EnumerateFiles(AppPaths.LogsDirectory, "*.log").Order().Select(File.ReadAllText)) : "";
    private static List<JsonElement> Reports() => Logs().Split('\n')
        .Where(l => l.Contains("operation=scan-baseline | phase=complete", StringComparison.Ordinal))
        .Select(l => JsonSerializer.Deserialize<JsonElement>(l[(l.IndexOf("details=", StringComparison.Ordinal) + 8)..])).ToList();
    private static void Count(Captured capture, string key, long expected)
    {
        var counts = capture.Report.GetProperty("Counts");
        Check(counts.TryGetProperty(key, out var count) && count.GetInt64() == expected, "Wrong/missing counter " + key + " expected " + expected);
    }
    private static void Outcome(Captured capture, string expected) =>
        Check(capture.Report.GetProperty("Outcome").GetString() == expected, "Wrong diagnostic outcome, expected " + expected);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

// In-process IDispatch doubles. Every entry point other than discovery/search is forbidden.
public sealed class CoverageWuaEnvironment
{
    public CoverageWuaResult Configured { get; set; } = new(2);
    public CoverageWuaResult Online { get; set; } = new(2);
    public CoverageWuaResult Microsoft { get; set; } = new(2);
    public bool HasMicrosoft { get; set; }
    public bool DiscoveryError { get; set; }
    public Exception? SearchError { get; set; }
    public Action? AfterSearch { get; set; }
    public List<int> Queried { get; } = [];
    public object Create(string progId) => progId switch
    {
        "Microsoft.Update.Session" => new CoverageWuaSession(this),
        "Microsoft.Update.ServiceManager" => DiscoveryError ? throw new COMException("Discovery unavailable") : new CoverageWuaManager(HasMicrosoft),
        _ => throw new InvalidOperationException("Unexpected COM operation: " + progId)
    };
}

public sealed class CoverageWuaSession(CoverageWuaEnvironment env)
{
    public string ClientApplicationID { get; set; } = "";
    public bool Online { get; set; }
    public bool IncludePotentiallySupersededUpdates { get; set; } = true;
    public int ServerSelection { get; set; }
    public string ServiceID { get; set; } = "";
    public CoverageWuaSession CreateUpdateSearcher() => this;
    public CoverageWuaResult Search(string criteria)
    {
        if (criteria != "IsInstalled=0 and IsHidden=0 and Type='Driver'" || !Online || IncludePotentiallySupersededUpdates ||
            (ServerSelection == 3 && ServiceID != "7971f918-a847-4430-9279-4a52d1efe18d"))
            throw new InvalidOperationException("Search policy changed");
        env.Queried.Add(ServerSelection);
        if (ServerSelection == 0 && env.SearchError is { } error) throw error;
        env.AfterSearch?.Invoke();
        return ServerSelection switch { 0 => env.Configured, 2 => env.Online, 3 => env.Microsoft, _ => throw new InvalidOperationException() };
    }
}

public sealed class CoverageWuaResult(int code, params FakeDriverOffer[] offers)
{
    public bool UnreadableStatus { get; set; }
    public bool UnreadableWarnings { get; set; }
    public int WarningCount { get; set; }
    public bool NullUpdates { get; set; }
    public Action<int>? ItemRead { get; set; }
    public int BadItem { get; set; } = -1;
    public int ResultCode => UnreadableStatus ? throw new COMException("Status unavailable") : code;
    public FakeWarningCollection Warnings => UnreadableWarnings ? throw new COMException("Warnings unavailable") : new() { Count = WarningCount };
    public CoverageWuaResult Updates => NullUpdates ? null! : this;
    public int Count => offers.Length;
    public FakeDriverOffer Item(int index)
    {
        ItemRead?.Invoke(index);
        return index == BadItem ? throw new COMException("Item unavailable") : offers[index];
    }
}

public sealed class CoverageWuaManager(bool microsoft)
{
    public CoverageWuaManager Services => this;
    public int Count => microsoft ? 1 : 0;
    public CoverageWuaManager Item(int index) => this;
    public string ServiceID => "7971f918-a847-4430-9279-4a52d1efe18d";
}
