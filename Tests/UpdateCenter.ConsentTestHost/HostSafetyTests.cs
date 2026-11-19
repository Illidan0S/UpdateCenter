using System.Reflection;
using UpdateCenter.Models;
using UpdateCenter.Services;

namespace UpdateCenter.ConsentTestHost;

internal static class HostSafetyTests
{
    internal static int Run()
    {
        // A new direct native call must be rejected before the manual host can start.
        var start = typeof(System.Diagnostics.Process).GetMethod("Start", [typeof(string)])!;
        Check(SafetyGuard.IsForbidden(start), "Process.Start must be rejected by the safety gate");
        Check(SafetyGuard.IsForbidden(typeof(System.Net.Http.HttpClient).GetMethod("GetAsync", [typeof(string)])!),
            "Network calls must be rejected");
        Check(SafetyGuard.IsForbidden(typeof(Type).GetMethod("GetTypeFromProgID", [typeof(string)])!),
            "COM activation must be rejected");
        SafetyGuard.Verify();
        Console.WriteLine("PASS compiled host has no native provider or forbidden direct API calls");
        foreach (var action in new Action[] {
            () => ElevatedUpdateRunner.RunWithConsentChannel("never-open", false, "never-connect", 0, 0),
            () => ReadOnlyProcess.Start(new System.Diagnostics.ProcessStartInfo("never-execute")),
            () => ProcessRunner.RunAsync("never-execute", []).GetAwaiter().GetResult(),
            () => WinGetService.Upgrade(new PlanItem(), true),
            () => WindowsUpdateService.InstallDriver(new PlanItem(), (_, _) => {}),
            () => OfficialDriverPackageService.Install(new PlanItem(), (_, _) => {}) })
        {
            try { action(); throw new Exception("Native adapter did not fail closed"); }
            catch (NativeOperationBlockedException) { }
        }
        Console.WriteLine("PASS native entry points fail closed (no process, provider or download)");
        Check(AppPaths.DataDirectory != AppPaths.ResolveDefaultRoot() &&
            Guid.TryParseExact(Path.GetFileName(AppPaths.DataDirectory), "N", out _), "Data root is not isolated");
        Console.WriteLine("PASS mandatory GUID data isolation");
        RunScenarioTests();
        return 0;
    }

    internal static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void RunScenarioTests()
    {
        foreach (var scenario in Enum.GetValues<ConsentScenario>())
        {
            using var cancel = new CancellationTokenSource();
            var session = new ScenarioSession(scenario, true, new TestDecision((_, _) => false), cancel);
            session.Run();
            Check(session.Calls.Count == 0 && session.Attempts.Count == 1, "Refusal reached effects or retried: " + scenario);
        }
        Console.WriteLine("PASS seven scenario refusals: zero effects, no retry after refusal");
        using (var cancel = new CancellationTokenSource())
        {
            var session = new ScenarioSession(ConsentScenario.Software, true, new TestDecision((_, _) => true), cancel);
            session.Run();
            Check(session.Calls.Count == 2 && session.Calls.All(c => c.Silent && !c.Interactive) &&
                session.Calls[0].Item!.Id == "Test.Software" && session.Calls[1].Item!.PackageOperation == "Install",
                "Software/runtime batch parameters changed");
            var changed = ScenarioSession.Software(); changed.AvailableVersion = "9.0";
            try { session.Record("software", changed, true, false); throw new Exception("Mismatch accepted"); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("differs")) { }
        }
        Console.WriteLine("PASS simulated provider receives approved identities/modes; mismatched target rejected");
        using (var cancel = new CancellationTokenSource())
        {
            var session = new ScenarioSession(ConsentScenario.ChangedFile, false, new TestDecision((_, _) => true), cancel);
            session.Run();
            Check(session.MutatedInputAfterRead && session.Calls.Single().Item!.AvailableVersion == "2.0" &&
                !session.Calls.Single().Silent, "Runner reread modified file or changed silent mode");
        }
        Console.WriteLine("PASS real runner uses frozen 2.0 after file changed to 9.0");
        using (var cancel = new CancellationTokenSource())
        {
            var prompts = 0;
            var session = new ScenarioSession(ConsentScenario.Retry, true, new TestDecision((_, _) => ++prompts < 3), cancel);
            session.Run();
            Check(prompts == 3 && session.Calls.Count == 2 && session.Calls[0].Operation == "software" &&
                session.Calls[1].Operation == "recovery", "Retry bypassed fresh consent");
        }
        Console.WriteLine("PASS retry refusal blocks second provider call after separately confirmed recovery");
        using (var cancel = new CancellationTokenSource())
        {
            var session = new ScenarioSession(ConsentScenario.Driver, true, new TestDecision((_, _) => true), cancel);
            session.Run();
            Check(session.Calls.Count == 2 && session.Calls[0].Operation == "restore" &&
                session.Calls[1].Item!.WindowsUpdateRevision == 3 && session.Calls[1].Item!.WindowsUpdateServerSelection == 3,
                "Driver snapshot or effect order changed");
        }
        Console.WriteLine("PASS driver/restore are recorded only; simulated privilege is not UAC validation");
        using (var cancel = new CancellationTokenSource())
        {
            var session = new ScenarioSession(ConsentScenario.Interactive, true, new TestDecision((_, _) => true), cancel);
            session.Run();
            Check(session.Calls.Single() is { Silent: false, Interactive: true }, "Fallback mode incorrect");
        }
        Console.WriteLine("PASS real fallback consent gate forwards interactive=true, silent=false");
        using (var cancel = new CancellationTokenSource())
        {
            var session = new ScenarioSession(ConsentScenario.Software, true,
                new TestDecision((_, _) => { cancel.Cancel(); return true; }), cancel);
            session.Run();
            Check(session.Calls.Count == 0, "Cancellation at consent boundary reached effects");
        }
        Console.WriteLine("PASS cancellation at approval boundary blocks all effects");
        Console.WriteLine("OFFLINE ONLY: callbacks above are not evidence of manual Windows interaction.");
    }

    private sealed class TestDecision(Func<RunnerPlanSnapshot, CancellationToken, bool> decide) : IRunnerConsent
    { public bool Confirm(RunnerPlanSnapshot plan, CancellationToken token) => decide(plan, token); }
}
