using System.Security.AccessControl;
using System.Security.Principal;

namespace UpdateCenter.Agent;

internal static class AgentSecretDirectory
{
    internal static void Secure(string root)
    {
        AssertNoReparsePoint(root);
        using var identity = WindowsIdentity.GetCurrent();
        var machineRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "UpdateCenter", "Agent");
        var isMachineStore = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))
            .Equals(machineRoot, StringComparison.OrdinalIgnoreCase);
        var trustees = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null)
        };
        if (!isMachineStore && !identity.IsSystem && identity.User is { } user) trustees.Add(user);
        // A writable/attacker-owned parent could replace the protected Agent directory.
        // Only protect this application's parent, never ProgramData or OS ancestors.
        if (isMachineStore) SecureDirectory(Path.GetDirectoryName(machineRoot)!, trustees);
        SecureDirectory(root, trustees);
        foreach (var name in new[] { "network-settings.json", "network-settings.json.tmp" })
        {
            var path = Path.Combine(root, name);
            AssertNoReparsePoint(path);
            if (!File.Exists(path)) continue;
            AssertTrustedOwner(new FileInfo(path).GetAccessControl(AccessControlSections.Owner), trustees);
            var fileSecurity = new FileSecurity();
            fileSecurity.SetAccessRuleProtection(true, false);
            foreach (var sid in trustees)
                fileSecurity.AddAccessRule(new FileSystemAccessRule(sid,
                    FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(fileSecurity);
        }
    }

    private static void SecureDirectory(string path, IReadOnlySet<SecurityIdentifier> trustees)
    {
        AssertNoReparsePoint(path);
        var directory = Directory.CreateDirectory(path);
        AssertNoReparsePoint(path);
        AssertTrustedOwner(directory.GetAccessControl(AccessControlSections.Owner), trustees);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var sid in trustees)
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    internal static void AssertTrustedOwner(FileSystemSecurity security,
        IReadOnlySet<SecurityIdentifier> trustedOwners)
    {
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner ||
            !trustedOwners.Contains(owner))
            throw new UnauthorizedAccessException("Il proprietario dei dati Agent non è fidato; riparare il percorso prima di avviare l'Agent.");
    }

    internal static void AssertNoReparsePoint(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null;
             current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Reparse point non ammesso nei dati Agent: " + current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
