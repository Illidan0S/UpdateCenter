using System.Reflection;
using System.Text.Json;
using UpdateCenter.Models;
using UpdateCenter.Services;

// RED evidence for the M6 contract, not a replacement implementation of an authorization gate.
// Only existing path validators, JSON storage and ToPlanItem run. The complete runner is NEVER
// invoked: an in-memory counter stands in for operations after admission. Provider rejection,
// UAC, UI consent and Windows process identity are deliberately outside this fixture's proof.
internal static class ConsentIntegrityEvidenceTests
{
    internal static void Run()
    {
        Check(!Path.GetFullPath(AppPaths.DataDirectory).Equals(Path.GetFullPath(AppPaths.ResolveDefaultRoot()),
            StringComparison.OrdinalIgnoreCase), "Production data root selected");
        Console.WriteLine("Isolated data root: " + AppPaths.DataDirectory);
        var failures = new List<string>();
        var passed = 0;
        void Case(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures.Add(name); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }

        Case("OriginalAuthorizedPlan", () =>
        {
            var (path, plan) = Fixture();
            JsonStorage.WriteAtomic(path, plan);
            var reached = 0;
            Check(AdmitAtCurrentFileBoundary(path, loaded =>
            {
                Check(loaded.Items.Single().Id == "Contoso.Tool" && loaded.Items[0].AvailableVersion == "2.0",
                    "Original operations changed");
                reached++;
            }) && reached == 1, "Original valid plan not admitted");
        });

        foreach (var (name, mutate) in new (string, Action<UpdatePlan>)[]
        {
            ("ChangedId", p => p.Items[0].Id = "Contoso.Other"),
            ("ChangedVersion", p => p.Items[0].AvailableVersion = "9.0"),
            ("ChangedSource", p => p.Items[0].Source = "other-source"),
            ("ChangedKind", p => p.Items[0].Kind = "Runtime"),
            ("ChangedOperation", p => p.Items[0].PackageOperation = PackageOperations.Install),
            ("ChangedSilentParameter", p => p.SilentSoftwareInstall = false),
            ("ChangedRestoreParameter", p => p.CreateRestorePoint = true),
            ("AddedUnapprovedItem", p => p.Items.Add(new PlanItem
                { Id = "Contoso.Added", Kind = "Software", Source = "winget", AvailableVersion = "4.0" }))
        })
        {
            Case(name, () =>
            {
                var (path, plan) = Fixture();
                JsonStorage.WriteAtomic(path, plan); // approved content
                mutate(plan);
                JsonStorage.WriteAtomic(path, plan); // same valid path, different operations
                RequireRejected(path);
            });
        }

        Case("InvalidJsonRejectedWithoutOperations", () =>
        {
            var (path, _) = Fixture();
            File.WriteAllText(path, "{\"Items\":[");
            RequireRejected(path);
        });
        Case("IncompleteItemRejectedWithoutOperations", () =>
        {
            var (path, plan) = Fixture();
            // Valid channel paths, but mandatory operation metadata absent.
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                plan.StatusFile, plan.PauseFile, Items = new[] { new { Name = "Incomplete" } }
            }));
            RequireRejected(path);
        });
        Case("AtomicReplacementBeforeHandoff", () =>
        {
            var (path, plan) = Fixture();
            JsonStorage.WriteAtomic(path, plan);
            var replacement = Path.Combine(AppPaths.DataDirectory, "replacement-" + Guid.NewGuid().ToString("N") + ".json");
            plan.Items[0].AvailableVersion = "8.0";
            JsonStorage.WriteAtomic(replacement, plan);
            File.Move(replacement, path, overwrite: true);
            RequireRejected(path);
        });
        Case("ReplacementAfterReadDoesNotChangeSnapshot", () =>
        {
            var (path, plan) = Fixture();
            JsonStorage.WriteAtomic(path, plan);
            var reached = 0;
            Check(AdmitAtCurrentFileBoundary(path, loaded =>
            {
                plan.Items[0].AvailableVersion = "8.0";
                JsonStorage.WriteAtomic(path, plan);
                Check(loaded.Items.Single().AvailableVersion == "2.0", "Loaded snapshot followed file replacement");
                reached++;
            }) && reached == 1, "Original snapshot not admitted");
        });
        Case("ConsumedPlanReplay", () =>
        {
            var (path, plan) = Fixture();
            JsonStorage.WriteAtomic(path, plan);
            Check(AdmitAtCurrentFileBoundary(path, _ => { }), "First admission failed");
            RequireRejected(path); // original authorization has been used
        });
        Case("RecreatedPreviousPlan", () =>
        {
            var (path, plan) = Fixture();
            JsonStorage.WriteAtomic(path, plan);
            var saved = File.ReadAllText(path);
            Check(AdmitAtCurrentFileBoundary(path, _ => { }), "First admission failed");
            File.Delete(path);
            File.WriteAllText(path, saved);
            RequireRejected(path);
        });
        foreach (var consent in new[] { "Absent", "Denied", "Cancelled" })
            Case("Consent" + consent, () =>
            {
                var (path, plan) = Fixture();
                // A file is injected directly; the normal UI was not used to authorize it.
                // The current file boundary has no consent input to distinguish these states.
                JsonStorage.WriteAtomic(path, plan);
                RequireRejected(path);
            });
        Case("OutsidePathRejectedWithoutOperations", () =>
            RequireRejected(Path.Combine(AppPaths.DataDirectory, "..", "update-plan-outside.json")));

        Console.WriteLine($"M6 admission contract: {passed} PASS, {failures.Count} FAIL; fake operation markers only, no real runner/provider/UAC.");
        if (failures.Count > 0)
            throw new InvalidOperationException("M6 BLOCKED: existing file intake cannot establish consent. Failed cases: " + string.Join(", ", failures));
    }

    private static (string Path, UpdatePlan Plan) Fixture()
    {
        AppPaths.EnsureCreated();
        var token = Guid.NewGuid().ToString("N");
        return (Path.Combine(AppPaths.DataDirectory, $"update-plan-{token}.json"), new UpdatePlan
        {
            SilentSoftwareInstall = true,
            StatusFile = Path.Combine(AppPaths.DataDirectory, $"update-status-{token}.json"),
            PauseFile = Path.Combine(AppPaths.DataDirectory, $"update-pause-{token}.signal"),
            PauseOwnerProcessId = Environment.ProcessId,
            Items = [ElevatedUpdateRunner.ToPlanItem(new UpdateItem
            {
                Id = "Contoso.Tool", Name = "Contoso Tool", Kind = UpdateKind.Software,
                Source = "winget", InstalledVersion = "1.0", AvailableVersion = "2.0"
            })]
        });
    }

    private static void RequireRejected(string path)
    {
        var reached = 0;
        var admitted = AdmitAtCurrentFileBoundary(path, plan => reached += plan.Items.Count);
        Check(!admitted && reached == 0, $"Unapproved input admitted; {reached} simulated operation(s) reachable");
    }

    private static bool AdmitAtCurrentFileBoundary(string path, Action<UpdatePlan> simulateOperations)
    {
        UpdatePlan plan;
        try
        {
            Validate("ValidatePlanPath", path);
            plan = JsonStorage.Read<UpdatePlan>(path) ?? throw new InvalidDataException("Invalid plan");
            Validate("ValidateStatusPath", plan.StatusFile);
            Validate("ValidatePausePath", plan.PauseFile);
        }
        catch (Exception ex) when (ex is InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
        simulateOperations(plan);
        return true;
    }

    private static void Validate(string method, string path)
    {
        try
        {
            typeof(ElevatedUpdateRunner).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [path]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
