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
            Check(!ReleasePublisherVerifier.IsOfficialLegacyTarget(original, "2.0.1"), "Withdrawn201IsNeverALegacyDestination");
            Check(!ReleasePublisherVerifier.IsOfficialLegacyTarget(original, "2.0.2"), "NoOpenEndedLegacyMigration");
        }
        // Change one byte in a copy, including if the input is a signed artifact.
        using (var altered = new MemoryStream(File.ReadAllBytes(officialPortable), writable: true))
        {
            altered.WriteByte(0);
            Check(!ReleasePublisherVerifier.IsOfficialLegacyTarget(altered, "2.0.0"), "AlteredPortable115Rejected");
            Check(!ReleasePublisherVerifier.IsOfficialLegacyTarget(altered, "2.0.1"), "AlteredPortable115RejectedFor201");
        }

        CheckWindowsAdmission();

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

        const string settings115 = """{"ThemeMode":"Scuro","LanguageMode":"en","AutomaticScanInterval":"Weekly","CheckAppUpdatesAutomatically":false,"DefaultsRevision":3}""";
        const string history115 = """[{"Timestamp":"2026-09-08T10:00:00Z","Name":"Fixture 1.1.5","Kind":"Software","FromVersion":"1","ToVersion":"2","Result":"Completato","Details":"existing history","Diagnostics":"fixture"}]""";
        File.WriteAllText(AppPaths.SettingsFile, settings115);
        File.WriteAllText(AppPaths.HistoryFile, history115);
        var settings = JsonStorage.LoadSettings();
        var previous = JsonStorage.LoadHistory().Single();
        Check(settings.ThemeMode == "Scuro" && settings.LanguageMode == "en" &&
            settings.AutomaticScanInterval == "Weekly" && !settings.CheckAppUpdatesAutomatically &&
            previous.Name == "Fixture 1.1.5" && previous.FromVersion == "1" && previous.ToVersion == "2" &&
            previous.Details == "existing history" && previous.PendingVerificationPlan is null &&
            File.ReadAllText(AppPaths.SettingsFile) == settings115 && File.ReadAllText(AppPaths.HistoryFile) == history115,
            "Legacy115SettingsAndHistorySurviveInPlaceUpdate");
    }

    internal static void CheckWindowsAdmission()
    {
        Check(SelfUpdateCompatibility.Supports(17763, 125, Architecture.X64, true) &&
            SelfUpdateCompatibility.Supports(19044, 191, Architecture.X64, true) &&
            SelfUpdateCompatibility.Supports(26200, 48, Architecture.X64, true) &&
            SelfUpdateCompatibility.Supports(26200, 188, Architecture.X64, true) &&
            !SelfUpdateCompatibility.Supports(19045, 48, Architecture.X64, true) &&
            !SelfUpdateCompatibility.Supports(26100, 48, Architecture.X64, true) &&
            !SelfUpdateCompatibility.Supports(26320, 0, Architecture.X64, true) &&
            !SelfUpdateCompatibility.Supports(26320, 188, Architecture.X64, true) &&
            !SelfUpdateCompatibility.Supports(26320, 48, Architecture.Arm64, true) &&
            !SelfUpdateCompatibility.Supports(26320, 48, Architecture.X64, false), "WindowsMatrixFailsClosed");
        foreach (var build in new[] { 26300, 26320, 28001 })
            Check(SelfUpdateCompatibility.Supports(build, 48, Architecture.X64, true), $"WindowsDesktopInsider{build}");
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
