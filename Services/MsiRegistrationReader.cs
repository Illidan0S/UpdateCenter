using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

// MSI query APIs only: no Win32_Product, package opening, configuration or repair.
internal static class MsiRegistrationReader
{
    internal static MsiRegistrationEvidence? Read(string localId, string displayVersion)
    {
        var parts = localId.Split('\\');
        if (parts.Length != 4 || !Guid.TryParse(parts[3], out var product)) return null;
        var code = product.ToString("B");
        using var current = WindowsIdentity.GetCurrent();
        var registrations = new List<MsiRegistrationEvidence>();
        // Enumerate on this thread: current-user managed/unmanaged and machine.
        // ERROR_NO_MORE_ITEMS is absence; any other error remains inconclusive.
        for (uint index = 0; index < 8; index++)
        {
            var found = new StringBuilder(39); var sid = new StringBuilder(256); uint size = 256;
            var error = MsiEnumProductsExW(code, null, 7, index, found, out var context, sid, ref size);
            if (error == 259) return Resolve(localId, displayVersion, current.User?.Value ?? "", registrations);
            if (error != 0 || !Guid.TryParse(found.ToString(), out var foundCode) || foundCode != product) return null;
            string? Property(string property)
            {
                var value = new StringBuilder(4096); uint count = 4096;
                return MsiGetProductInfoExW(code, context == 4 ? null : sid.ToString(), context, property, value, ref count) == 0
                    ? value.ToString() : null;
            }
            if (Property("State") != "5" || Property("Transforms") != "" || Property("InstanceType") != "0") return null;
            var version = Property("VersionString"); var language = Property("Language"); var package = Property("PackageCode");
            if (version is null || language is null || package is null) return null;
            registrations.Add(new(code, (int)context, sid.ToString(), version, language, package));
        }
        return null;
    }

    internal static MsiRegistrationEvidence? Resolve(string localId, string rawVersion, string currentSid,
        IReadOnlyList<MsiRegistrationEvidence>? registrations) =>
        registrations is { Count: 1 } && Matches(registrations[0], localId, rawVersion, currentSid) ? registrations[0] : null;

    internal static bool Valid(MsiRegistrationEvidence evidence, string rawVersion) =>
        Guid.TryParse(evidence.ProductCode, out var product) && product != Guid.Empty &&
        Guid.TryParse(evidence.PackageCode, out var package) && package != Guid.Empty &&
        evidence.Context is 1 or 2 or 4 &&
        (evidence.Context == 4 ? evidence.UserSid == "" : Regex.IsMatch(evidence.UserSid ?? "", @"^S-1-\d+(?:-\d+)+$")) &&
        ushort.TryParse(evidence.Language, out var language) && language != 0 &&
        // This compares two observations of the installed version, not the MSI
        // Upgrade table's three-field ordering against a different target.
        SoftwareVersionEvidence.Compare(evidence.ProductVersion, rawVersion) == SoftwareVersionRelation.Equal;

    internal static bool Matches(MsiRegistrationEvidence evidence, string localId, string rawVersion, string currentSid)
    {
        var parts = localId.Split('\\');
        return Valid(evidence, rawVersion) && parts.Length == 4 && parts[0] == "ARP" &&
            Guid.TryParse(parts[3], out var product) && product == Guid.Parse(evidence.ProductCode) &&
            (evidence.Context == 4 ? parts[1] == "Machine" : parts[1] == "User" && evidence.UserSid == currentSid);
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiEnumProductsExW(string product, string? userSid, uint contexts, uint index,
        StringBuilder installedProduct, out uint installedContext, StringBuilder sid, ref uint sidLength);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiGetProductInfoExW(string product, string? userSid, uint context, string property,
        StringBuilder value, ref uint length);
}
