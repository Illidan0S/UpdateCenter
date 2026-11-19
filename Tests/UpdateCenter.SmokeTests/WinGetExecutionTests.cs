using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class WinGetExecutionTests
{
    public static void Run()
    {
        if (Environment.GetCommandLineArgs().Contains("--b2-read-only-inventory", StringComparer.Ordinal))
        {
            WinGetB2Inventory.RunAsync().GetAwaiter().GetResult();
            return;
        }
        WinGetScopeSafetyTests.Run();
        WinGetScopedExecutionTests.Run();
        WinGetB2CoverageTests.Run();
        const string target = "2026.07.29-8228f98";
        var item = new PlanItem { Id = "Contoso.Tool", Name = "Contoso Tool", Source = "winget", AvailableVersion = target };
        var calls = new List<string[]>();
        var problems = new List<string>();
        void Check(bool pass, string message) { if (!pass) problems.Add(message); }
        var fallback = false;
        Task<ProcessResult> Capture(IReadOnlyList<string> arguments, CancellationToken token, TimeSpan timeout)
        {
            calls.Add(arguments.ToArray());
            if (fallback && arguments[0] == "upgrade" && arguments.Contains("--id"))
                return Task.FromResult(new ProcessResult(1, "", "No installed package found matching input criteria"));
            if (arguments[0] == "list")
                return Task.FromResult(new ProcessResult(0, arguments.Contains("--name")
                    ? "Name          Id            Version\n-----------------------------------\nContoso Tool  Contoso.Tool  1.0\n"
                    : "", ""));
            return Task.FromResult(new ProcessResult(0, "", ""));
        }

        void CheckDispatch(string operation, string selector, string expectedValue)
        {
            var arguments = calls.Single();
            var selectorIndex = Array.IndexOf(arguments, selector);
            Check(arguments[0] == operation && arguments.Contains("--exact") &&
                selectorIndex >= 0 && arguments[selectorIndex + 1] == expectedValue, $"{operation}: wrong package selector");
            CheckPin(arguments);
            calls.Clear();
        }
        void CheckPin(string[] arguments)
        {
            var index = Array.IndexOf(arguments, "--version");
            Check(index >= 0 && index + 1 < arguments.Length && arguments[index + 1] == target &&
                arguments.Count(x => x == "--version") == 1, $"Missing or changed target: {string.Join(' ', arguments)}");
        }

        WinGetService.Install(item, true, Capture);
        CheckDispatch("install", "--id", item.Id);
        WinGetService.Upgrade(item, false, Capture);
        CheckDispatch("upgrade", "--id", item.Id);
        foreach (var (operation, verb) in new[] { (PackageOperations.Install, "install"), (PackageOperations.Upgrade, "upgrade") })
        {
            item.PackageOperation = operation;
            WinGetService.RunInteractive(item, Capture);
            Check(calls.Single().Contains("--interactive"), "Interactive retry lost --interactive");
            CheckDispatch(verb, "--id", item.Id);
        }

        fallback = true;
        var result = WinGetService.Upgrade(item, true, Capture);
        Check(!result.Success && calls.Count == 2 && calls[0].Contains("--source") &&
            calls[1][0] == "list" && calls[1].Contains("--source") && calls[1].Contains("--id") &&
            calls.All(a => !a.Contains("--name")),
            "An unmatched approved source/ID must stop execution; only scoped inventory reconciliation is allowed");
        foreach (var args in calls.Where(a => a[0] == "upgrade")) CheckPin(args);
        calls.Clear();

        foreach (var badVersion in new[] { "", "   ", "Unknown", "Sconosciuta", "Latest", "Più recente", "N/A", "—", "--silent", "/force", "1.2\n3", "2.*", "1.2…" })
        {
            item.AvailableVersion = badVersion;
            var install = WinGetService.Install(item, true, Capture);
            var upgrade = WinGetService.Upgrade(item, true, Capture);
            var interactive = WinGetService.RunInteractive(item, Capture);
            Check(!install.Success && !upgrade.Success && !interactive.Success && calls.Count == 0,
                $"Unavailable or unsafe target dispatched {calls.Count} processes: [{badVersion}]");
            calls.Clear();
        }
        foreach (var output in new[] { "Publisher: Contoso", "Version: Unknown", "Versione:\r\n2.0", "Version: --force" })
            Check(WinGetService.ParsePackageAvailability(item.Id, output) is null, $"Unresolved offer usable: [{output}]");
        Check(WinGetService.ParsePackageAvailability(item.Id, $"Versione: {target}\r\nPublisher: Contoso")?.Version == target,
            "Explicit catalog version was lost");
        foreach (var (version, valid) in new[] { ("v1.0.12576", true), ("v1.13.123-preview", true), ("garbage", false) })
        {
            var scanCalls = new List<string>();
            var scanner = new WinGetService((args, _, _) =>
            {
                scanCalls.Add(args[0]);
                return Task.FromResult(new ProcessResult(0, args[0] == "--version" ? version : "No applicable upgrade found", ""));
            });
            var accepted = true;
            try { scanner.ScanAsync(false, default).GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { accepted = false; }
            Check(accepted == valid && scanCalls.SequenceEqual(valid ? ["--version", "upgrade"] : new[] { "--version" }),
                $"Client identification accepted={accepted}, calls={string.Join(' ', scanCalls)} for [{version}]");
        }
        if (problems.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
    }
}
