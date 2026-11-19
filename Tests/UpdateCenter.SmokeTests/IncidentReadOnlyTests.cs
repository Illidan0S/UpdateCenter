using System.Runtime.InteropServices;
using System.Security.Principal;
using UpdateCenter.Models;
using UpdateCenter.Services;

// Explicit operator mode, never part of the offline suite. No runner, installer,
// deferred ticket reconciliation or production-history writes are reachable.
internal static class IncidentReadOnlyTests
{
    internal static async Task RunAsync()
    {
        if (AppPaths.DataDirectory == AppPaths.ResolveDefaultRoot()) throw new InvalidOperationException("Production root selected.");
        using var account = WindowsIdentity.GetCurrent();
        var sid = account.User!.Value;
        foreach (var (id, target) in new[] { ("Ubisoft.Connect", "175.1.0.13416"), ("Moonsworth.LunarClient", "3.7.28") })
        {
            var manifest = WinGetScopedExecutionTests.Manifest(id);
            string[] Query(string scope) => ["list", "--id", id, "--exact", "--source", "winget",
                .. (scope.Length == 0 ? Array.Empty<string>() : ["--scope", scope]), "--details", "--disable-interactivity", "--nowarn"];
            var allowed = new[] { new[] { "--version" }, Query(""), Query("user"), Query("machine") };
            Task<ProcessResult> Execute(IReadOnlyList<string> args, CancellationToken token, TimeSpan timeout)
            {
                if (!allowed.Any(command => command.SequenceEqual(args))) throw new InvalidOperationException("Non-read-only command rejected.");
                return ProcessRunner.RunAsync("winget.exe", args, token, timeout);
            }
            var query = await Execute(Query(""), default, TimeSpan.FromSeconds(45));
            var identity = query.Success ? WinGetScopeGuard.ParseIdentity(query.StandardOutput, id) : null;
            if (identity is null) throw new InvalidOperationException("Native identity unavailable: " + id);
            var guard = new WinGetScopeGuard(Execute, (package, version, _) => Task.FromResult<string?>(package == id && version == target ? manifest : null),
                () => sid, WinGetInstalledScopeService.Detect, RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
                readDisplayVersion: WinGetInstalledScopeService.ReadDisplayVersion, readCopyEvidence: WinGetCopyEvidenceReader.Read);
            var item = new UpdateItem { Id = id, Name = identity.Name, Kind = UpdateKind.Software,
                Source = "winget", InstalledVersion = identity.Version, AvailableVersion = target };
            item.WinGetBinding = await guard.PrepareAsync(item, manifest, default);
            if (item.WinGetBinding is null) throw new InvalidOperationException("Native copy binding unavailable: " + id);
            var result = await guard.VerifyAsync(ElevatedUpdateRunner.ToPlanItem(item));
            Console.WriteLine($"{id}: Verified={result.Verified}; LocalId={item.WinGetBinding.LocalId}; Raw={item.WinGetBinding.CopyEvidence?.DisplayVersion}; PE={item.WinGetBinding.CopyEvidence?.ExecutablePath}");
            Console.WriteLine(result.Diagnostics);
            if (!result.Verified) throw new InvalidOperationException("Native current target was not verified: " + id);
        }
        Console.WriteLine("PASS current native copies verified read-only; no installer, runner or production-history mutation.");
    }
}
