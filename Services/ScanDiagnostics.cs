using System.Diagnostics;
using System.Text.Json;

namespace UpdateCenter.Services;

// Local, explicitly enabled diagnostics. Only fixed labels and numeric counts belong here.
// No package/device identity, exception text, command line or remote endpoint is collected.
internal sealed class ScanDiagnostics : IDisposable
{
    private static readonly AsyncLocal<ScanDiagnostics?> Current = new();
    private readonly ScanDiagnostics? _previous;
    private readonly object _gate = new();
    private readonly List<Phase> _phases = [];
    private readonly Dictionary<string, long> _counts = [];
    private readonly ResourceSample _resources = ResourceSample.Read();
    private readonly long _started = Stopwatch.GetTimestamp();
    private bool _closed;
    private string _outcome = "error";

    private ScanDiagnostics() => _previous = Current.Value;

    internal static ScanDiagnostics? Start()
    {
        if (Environment.GetEnvironmentVariable("UPDATECENTER_SCAN_DIAGNOSTICS") != "1") return null;
        var scan = new ScanDiagnostics();
        Current.Value = scan;
        return scan;
    }

    internal void Complete(string outcome) => _outcome = outcome;

    internal static bool IsActive => Current.Value is not null;

    // Avoid constructing per-provider counter names when diagnostics are disabled.
    internal static void Count(string prefix, string name, long value)
    {
        if (Current.Value is not null) Count(prefix + "." + name, value);
    }

    internal static Phase? Begin(string name, CancellationToken cancellationToken = default)
    {
        var scan = Current.Value;
        if (scan is null) return null;
        lock (scan._gate)
        {
            if (scan._closed) return null;
            var phase = new Phase(scan, name, cancellationToken);
            scan._phases.Add(phase);
            return phase;
        }
    }

    internal static void Count(string name, long value)
    {
        var scan = Current.Value;
        if (scan is null) return;
        lock (scan._gate)
        {
            if (!scan._closed) scan._counts[name] = scan._counts.GetValueOrDefault(name) + value;
        }
    }

    // Invoke synchronously before wrapping the returned task: synchronous provider failures
    // must still escape at the original call site (not be deferred until a later await).
    internal static Task<T> ObserveAsync<T>(string name, Func<Task<T>> operation, CancellationToken token)
    {
        var phase = Begin(name, token);
        if (phase is null) return operation();
        try
        {
            var task = operation();
            if (task.IsCompletedSuccessfully)
            {
                phase.Complete();
                phase.Dispose();
                return task;
            }
            return ObserveCompletionAsync(task, phase);
        }
        catch
        {
            phase.Dispose();
            throw;
        }
    }

    private static async Task<T> ObserveCompletionAsync<T>(Task<T> task, Phase phase)
    {
        using (phase)
        {
            var result = await task.ConfigureAwait(false);
            phase.Complete();
            return result;
        }
    }

    public void Dispose()
    {
        if (ReferenceEquals(Current.Value, this)) Current.Value = _previous;
        try
        {
            var resources = ResourceSample.Read();
            object report;
            lock (_gate)
            {
                if (_closed) return;
                _closed = true;
                var ended = Stopwatch.GetTimestamp();
                var pending = _phases.Count(p => p.Ended is null);
                // A cancelled scan may leave a provider finishing later. Report a clipped
                // interval as incomplete, never invent completion or wait for it here.
                var phases = _phases.Select(p => new
                {
                    p.Name,
                    StartMs = Stopwatch.GetElapsedTime(_started, p.Started).TotalMilliseconds,
                    DurationMs = Stopwatch.GetElapsedTime(p.Started, p.Ended ?? ended).TotalMilliseconds,
                    Outcome = p.Ended is null ? "incomplete" : p.Outcome
                }).ToArray();
                var outcome = _outcome == "success" && phases.Any(p => p.Outcome is "error" or "partial" or "incomplete")
                    ? "partial" : _outcome;
                report = new
                {
                    SchemaVersion = 1, Outcome = outcome,
                    TotalMs = Stopwatch.GetElapsedTime(_started, ended).TotalMilliseconds,
                    PendingPhases = pending,
                    Process = new
                    {
                        CpuMs = resources.CpuMs - _resources.CpuMs,
                        AllocatedBytes = resources.AllocatedBytes - _resources.AllocatedBytes,
                        WorkingSetStartBytes = _resources.WorkingSetBytes,
                        WorkingSetEndBytes = resources.WorkingSetBytes,
                        PrivateStartBytes = _resources.PrivateBytes,
                        PrivateEndBytes = resources.PrivateBytes
                    },
                    Counts = _counts, Phases = phases
                };
                LogService.WriteEvent("scan-baseline", "complete", outcome, details: JsonSerializer.Serialize(report));
            }
        }
        catch
        {
            // Diagnostics must never replace an application result or exception.
        }
    }

    internal sealed class Phase : IDisposable
    {
        private readonly ScanDiagnostics _scan;
        private readonly CancellationToken _token;
        internal string Name { get; }
        internal long Started { get; } = Stopwatch.GetTimestamp();
        internal long? Ended { get; private set; }
        internal string Outcome { get; private set; } = "error";

        internal Phase(ScanDiagnostics scan, string name, CancellationToken token)
        {
            _scan = scan;
            Name = name;
            _token = token;
        }

        internal void Complete(string outcome = "success")
        {
            lock (_scan._gate)
                if (!_scan._closed && Ended is null) Outcome = outcome;
        }

        public void Dispose()
        {
            lock (_scan._gate)
            {
                if (_scan._closed || Ended is not null) return;
                if (Outcome == "error" && _token.IsCancellationRequested) Outcome = "cancelled";
                Ended = Stopwatch.GetTimestamp();
            }
        }
    }

    private sealed record ResourceSample(double? CpuMs, long? WorkingSetBytes, long? PrivateBytes, long AllocatedBytes)
    {
        internal static ResourceSample Read()
        {
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            try
            {
                using var process = Process.GetCurrentProcess();
                return new(process.TotalProcessorTime.TotalMilliseconds, process.WorkingSet64,
                    process.PrivateMemorySize64, allocated);
            }
            catch { return new(null, null, null, allocated); }
        }
    }
}
