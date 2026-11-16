using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class R3DriverTests
{
    private const string HardwareId = @"PCI\VEN_1234&DEV_5678";
    private static PlanItem Plan(string target = "2.0") => new()
    {
        Id = "Fixture.Driver", Name = "Fixture driver", Kind = "Driver", AvailableVersion = target,
        CompatibleHardwareIds = [HardwareId], DriverDeviceId = HardwareId, InstalledVersion = "1.0"
    };

    private static DriverInventoryItem Device(string version, string inf = "oem42.inf") => new()
    {
        DeviceName = "Fixture device", DeviceId = HardwareId, HardwareIds = [HardwareId],
        InstalledVersion = version, InfName = inf, IsSigned = true
    };

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Inconclusive(UpdateVerificationResult result, string description)
    {
        Check(!result.Verified && !result.IsDefinitive && result.Status == UpdateVerificationStatuses.Unavailable,
            description + " must be inconclusive.");
    }

    internal static void Run()
    {
        Check(AppPaths.DataDirectory != AppPaths.ResolveDefaultRoot(), "Production data root selected.");
        var failures = new List<string>();
        foreach (var (name, test) in new (string, Action)[]
        {
            ("duplicate devices", () => Inconclusive(OfficialDriverPackageService.EvaluateInstalledDrivers(
                Plan(), false, [Device("1.0"), Device("2.0")]), "Two devices sharing a hardware ID")),
            ("missing target", () => Inconclusive(OfficialDriverPackageService.EvaluateInstalledDrivers(
                Plan(""), false, [Device("2.0")]), "Missing target")),
            ("non-numeric target", () => Inconclusive(OfficialDriverPackageService.EvaluateInstalledDrivers(
                Plan("2.0-beta"), false, [Device("2.0")]), "Non-numeric target")),
            ("missing installed version", () => Inconclusive(OfficialDriverPackageService.EvaluateInstalledDrivers(
                Plan(), false, [Device("")]), "Missing installed version")),
            ("missing active INF", () => Inconclusive(OfficialDriverPackageService.EvaluateInstalledDrivers(
                Plan(), false, [Device("2.0", "")]), "Missing active INF")),
            ("aggregated devices", () => {
                var device = Device("2.0"); device.Quantity = 2;
                Inconclusive(OfficialDriverPackageService.EvaluateInstalledDrivers(Plan(), false, [device]),
                    "Aggregated inventory row"); }),
            ("missing device ID", () => {
                var device = Device("2.0"); device.DeviceId = "";
                Inconclusive(OfficialDriverPackageService.EvaluateInstalledDrivers(Plan(), false, [device]),
                    "Missing device instance ID"); }),
            ("different device instance", () => {
                var device = Device("2.0"); device.DeviceId = HardwareId + @"\OTHER";
                Inconclusive(OfficialDriverPackageService.EvaluateInstalledDrivers(Plan(), false, [device]),
                    "A different instance with the same compatible hardware ID"); }),
            ("missing planned device instance", () => {
                var plan = Plan(); plan.DriverDeviceId = "";
                Inconclusive(OfficialDriverPackageService.EvaluateInstalledDrivers(plan, false, [Device("2.0")]),
                    "Missing planned device instance"); }),
            ("missing identity is a preflight exclusion", () => {
                var plan = Plan(); plan.DriverDeviceId = "";
                var result = OfficialDriverPackageService.Install(plan);
                Check(result.Outcome == UpdateOutcomes.ManualRequired && !result.InstallerSucceeded && !result.Verified,
                    "Missing identity was not reported as a preflight exclusion."); }),
            ("unsigned active driver", () => {
                var device = Device("2.0"); device.IsSigned = false;
                Inconclusive(OfficialDriverPackageService.EvaluateInstalledDrivers(Plan(), false, [device]),
                    "Unsigned active driver"); }),
            ("no matching device", () => Inconclusive(OfficialDriverPackageService.EvaluateInstalledDrivers(
                Plan(), false, []), "No matching device in aggregated inventory")),
            ("unique current device", () => {
                var result = OfficialDriverPackageService.EvaluateInstalledDrivers(Plan(), false, [Device("2.1")]);
                Check(result.Verified && result.IsDefinitive && result.Status == UpdateVerificationStatuses.Verified,
                    "Unique matching active driver at newer version was not verified."); }),
            ("unique old device", () => {
                var result = OfficialDriverPackageService.EvaluateInstalledDrivers(Plan(), false, [Device("1.0")]);
                Check(!result.Verified && result.IsDefinitive && result.Status == UpdateVerificationStatuses.Failed,
                    "Unique matching old active driver was not a definitive failure."); }),
            ("old device pending restart", () => {
                var result = OfficialDriverPackageService.EvaluateInstalledDrivers(Plan(), true, [Device("1.0")]);
                Check(!result.Verified && !result.IsDefinitive && result.Status == UpdateVerificationStatuses.PendingRestart,
                    "Old driver awaiting restart was not kept pending."); }),
            ("device binding frozen across plan", () => {
                var proposal = new UpdateItem { Id = "Fixture.Driver", Name = "Fixture", Kind = UpdateKind.Driver,
                    DriverDeviceId = HardwareId };
                var plan = ElevatedUpdateRunner.ToPlanItem(proposal);
                Check(plan.DriverDeviceId == HardwareId, "Plan lost the selected device instance.");
                var frozen = new RunnerPlanItem(plan);
                plan.DriverDeviceId = HardwareId + @"\OTHER";
                Check(frozen.ToPlanItem().DriverDeviceId == HardwareId && frozen.ToUpdateItem().DriverDeviceId == HardwareId,
                    "Snapshot lost or changed the selected device instance.");
                Check(!RunnerPlanItem.Matches(proposal, plan), "Device instance mutation matched the approved proposal."); }),
            ("device preflight accepts bound inventory", () =>
                OfficialDriverPackageService.ValidateInstalledDevice(Plan(), [Device("1.0")])),
            ("device preflight rejects replacement", () => {
                var device = Device("1.0"); device.DeviceId += @"\OTHER";
                RejectPreflight(Plan(), [device], "Different device instance"); }),
            ("device preflight rejects version drift", () =>
                RejectPreflight(Plan(), [Device("1.1")], "Changed installed version")),
            ("device preflight rejects duplicates", () =>
                RejectPreflight(Plan(), [Device("1.0"), Device("1.0")], "Multiple compatible devices"))
        })
        {
            try { test(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); Console.WriteLine("FAIL " + failures[^1]); }
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        Console.WriteLine("PASS R3 driver verification evidence (synthetic inventory only).");
    }

    private static void RejectPreflight(PlanItem plan, IReadOnlyList<DriverInventoryItem> drivers, string description)
    {
        try { OfficialDriverPackageService.ValidateInstalledDevice(plan, drivers); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException(description + " reached the install boundary.");
    }
}
