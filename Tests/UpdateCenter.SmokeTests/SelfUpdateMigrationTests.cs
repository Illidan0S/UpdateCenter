using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class SelfUpdateMigrationTests
{
    internal static void Run(string officialPortable)
    {
        using (var original = File.OpenRead(officialPortable))
        {
            Check(ReleasePublisherVerifier.IsOfficialLegacyTarget(original, "2.0.0"), "OfficialPortable115Recognized");
            Check(!ReleasePublisherVerifier.IsOfficialLegacyTarget(original, "2.0.1"), "LegacyExceptionLimitedTo200");
        }
        // Change one byte in a copy, including if the input is a signed artifact.
        using (var altered = new MemoryStream(File.ReadAllBytes(officialPortable), writable: true))
        {
            altered.WriteByte(0);
            Check(!ReleasePublisherVerifier.IsOfficialLegacyTarget(altered, "2.0.0"), "AlteredPortable115Rejected");
        }

        Check(SelfUpdateCompatibility.Supports(17763, 125, Architecture.X64, true) &&
            SelfUpdateCompatibility.Supports(19044, 191, Architecture.X64, true) &&
            SelfUpdateCompatibility.Supports(26200, 48, Architecture.X64, true) &&
            !SelfUpdateCompatibility.Supports(19045, 48, Architecture.X64, true) &&
            !SelfUpdateCompatibility.Supports(26100, 48, Architecture.X64, true) &&
            !SelfUpdateCompatibility.Supports(26200, 48, Architecture.Arm64, true) &&
            !SelfUpdateCompatibility.Supports(26200, 48, Architecture.X64, false) &&
            SelfUpdateCompatibility.Supports(26320, 48, Architecture.X64, true), "WindowsMatrixPreservesPlatformGuards");

        try
        {
            ReleasePublisherVerifier.VerifySignaturesAsync([officialPortable], new string('0', 64), CancellationToken.None)
                .GetAwaiter().GetResult();
            throw new InvalidOperationException("Unsigned candidate accepted.");
        }
        catch (UnauthorizedAccessException) { Console.WriteLine("PASS UnsignedCandidateRejected"); }

        var root = Path.Combine(AppPaths.DataDirectory, "portable-fixture");
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, "UpdateCenter.exe");
        var staged = target + ".update-new";
        var backup = target + ".update-backup";
        var data = new[] { "settings.json", "history.json", "recent.log" };
        foreach (var name in data) File.WriteAllText(Path.Combine(root, name), name);
        File.WriteAllText(target, "old");
        File.WriteAllText(staged, "new");
        var originalHash = SHA256.HashData("old"u8);
        var candidateHash = SHA256.HashData("new"u8);
        if (!SelfUpdateCompatibility.IsSupported())
        {
            var result = AppUpdateService.ApplyUpdate(target, Environment.ProcessId, "2.0.0");
            Check(result != 0 && File.ReadAllText(target) == "old" && !File.Exists(backup) &&
                !Directory.Exists(AppPaths.LogsDirectory), "UnsupportedHostRefusesBeforeWriting");
        }
        try
        {
            PortableUpdateTransaction.Apply(target, staged, backup, originalHash, candidateHash, () =>
            {
                Check(File.ReadAllText(target) == "new" && File.ReadAllText(backup) == "old", "BackupRetainedDuringStartup");
                ConfirmChild(prematureExit: true);
            });
            throw new InvalidOperationException("Startup timeout was accepted.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("prima di inizializzare", StringComparison.Ordinal)) { }
        Check(File.ReadAllText(target) == "old" && !File.Exists(backup), "FailedStartupRestoresOriginal");

        File.WriteAllText(staged, "new");
        PortableUpdateTransaction.Apply(target, staged, backup, originalHash, candidateHash, () => ConfirmChild(prematureExit: false));
        Check(File.ReadAllText(target) == "new" && !File.Exists(backup), "ConfirmedStartupDiscardsBackup");
        Check(data.All(name => File.ReadAllText(Path.Combine(root, name)) == name), "MigrationPreservesDataFixtures");

        Directory.CreateDirectory(AppPaths.LogsDirectory);
        var oldLog = Path.Combine(AppPaths.LogsDirectory, "UpdateCenter-fixture.log");
        File.WriteAllText(oldLog, "original log");
        File.SetLastWriteTimeUtc(oldLog, DateTime.UtcNow.AddDays(-45));
        File.WriteAllText(AppPaths.SettingsFile, "{}");
        File.WriteAllText(AppPaths.HistoryFile, "[]");
        AppPaths.UpdateStartupPending = true;
        try
        {
            AppPaths.EnsureCreated();
            JsonStorage.SaveSettings(new AppSettings());
            JsonStorage.SaveHistory([new HistoryEntry()]);
            LogService.Write("Uncommitted startup log.");
            Check(File.ReadAllText(AppPaths.SettingsFile) == "{}" &&
                File.ReadAllText(AppPaths.HistoryFile) == "[]" &&
                Directory.GetFiles(AppPaths.LogsDirectory).Length == 1 && File.ReadAllText(oldLog) == "original log",
                "PendingStartupDoesNotWriteProfileData");
        }
        finally { AppPaths.UpdateStartupPending = false; }
    }

    internal static Task ChildAsync(string name, string supervisorId) =>
        PortableUpdateStartup.IsValidName(name) && int.TryParse(supervisorId, out var pid) && pid > 0
            ? PortableUpdateStartup.SignalWindowRenderedAsync(name, pid) : Task.CompletedTask;

    private static void ConfirmChild(bool prematureExit)
    {
        var name = PortableUpdateStartup.CreateName();
        using var pipe = RunnerConsentChannel.Create(name);
        var executable = Environment.ProcessPath!;
        if (!Path.GetFileName(executable).Equals("UpdateCenter.SmokeTests.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Start the smoke apphost directly for this test.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--self-update-startup-child");
        start.ArgumentList.Add(prematureExit ? "exit-before-ready" : name);
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Fixture process did not start.");
        PortableUpdateStartup.ConfirmAsync(pipe, child, TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();
        if (!child.WaitForExit(5000) || child.ExitCode != 0)
            throw new InvalidOperationException("Fixture process did not receive the commit acknowledgement.");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name);
    }
}
