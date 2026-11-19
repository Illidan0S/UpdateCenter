using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UpdateCenter.Services;

internal static class SelfUpdateCompatibilityTests
{
    internal static void Run(string repositoryRoot)
    {
        var failures = 0;
        var checks = 0;
        void Check(bool condition, string name)
        {
            checks++;
            if (!condition) failures++;
            Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name}");
        }

        // Synthetic OS inputs: these do not attest an installation on those systems.
        (string Name, int Build, int Sku, bool Accepted)[] cases =
        [
            ("Windows10Ltsc2019", 17763, 125, true),
            ("Windows10Ltsc2019N", 17763, 126, true),
            ("Windows10Ltsc2021", 19044, 125, true),
            ("Windows10IotLtsc2021", 19044, 191, true),
            ("Windows11Enterprise24H2", 26100, 4, true),
            ("Windows11Education24H2", 26100, 121, true),
            ("Windows11Ltsc2024", 26100, 125, true),
            ("Windows11IotLtsc2024", 26100, 191, true),
            ("Windows11Pro25H2", 26200, 48, true),
            ("Windows11Home25H2", 26200, 101, true),
            ("Windows11Iot25H2", 26200, 188, true),
            ("Windows11Pro26H2", 26300, 48, true),
            ("Windows11Pro26H1", 28000, 48, true),
            ("InsiderBeta26320Pro", 26320, 48, true),
            ("InsiderBeta26320Home", 26320, 101, true),
            ("InsiderBeta26320Enterprise", 26320, 4, true),
            ("InsiderBeta26320Education", 26320, 121, true),
            ("InsiderAdjacentBuild", 26321, 48, true),
            ("InsiderLaterBuild", 28001, 48, true),
            ("Windows7Rejected", 7601, 48, false),
            ("Windows10Pro1809Rejected", 17763, 48, false),
            ("Windows10Pro21H2Rejected", 19044, 48, false),
            ("Windows10Pro22H2Rejected", 19045, 48, false),
            ("Windows11Pro23H2Rejected", 22631, 48, false),
            ("Windows11Pro24H2Rejected", 26100, 48, false),
            ("BelowDesktopFloorRejected", 26199, 48, false),
            ("UnknownEditionRejected", 26320, 0, false),
            ("ServerEditionRejected", 26320, 7, false),
            ("UnknownLtscBuildRejected", 26320, 125, false),
            ("Iot26H1StillRejected", 28000, 188, false)
        ];
        foreach (var item in cases)
            Check(SelfUpdateCompatibility.Supports(item.Build, item.Sku, Architecture.X64, true) == item.Accepted,
                item.Name);

        foreach (var build in new[] { 17763, 19044, 26100, 26200, 26320, 28000 })
        {
            var sku = build < 26200 ? 125 : 48;
            foreach (var architecture in new[] { Architecture.X86, Architecture.Arm, Architecture.Arm64 })
                Check(!SelfUpdateCompatibility.Supports(build, sku, architecture, true),
                    $"NonX64Rejected_{build}_{architecture}");
            Check(!SelfUpdateCompatibility.Supports(build, sku, Architecture.X64, false),
                $"NonClientRejected_{build}");
        }

        // Static regression guards only: compiling the script and exercising the
        // Setup callbacks at runtime are separate evidence.
        var installer = File.ReadAllText(Path.Combine(repositoryRoot, "installer.iss"));
        var checksum = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "UnsignedSetupChecksum.iss"));
        Check(Regex.IsMatch(installer,
            @"procedure DeinitializeSetup;\s*begin\s*if VerifiedSetupSource <> nil then\s*begin\s*VerifiedSetupSource.Free;\s*VerifiedSetupSource := nil;\s*end;\s*end;"),
            "Static_SetupAbortCleanupGuardsEmptyHandle");
        Check(Regex.IsMatch(checksum,
            @"VerifiedSetupSource := Source;\s*Source := nil;\s*Result := True;\s*finally\s*if Source <> nil then Source.Free;"),
            "Static_ChecksumTransferRetainsVerifiedHandle");

        Console.WriteLine($"Self-update platform checks: {checks - failures}/{checks} PASS.");
        if (failures != 0) throw new InvalidOperationException($"{failures} platform regression checks failed.");
    }
}
