using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace UpdateCenter.Services;

internal enum RestartManagerApplicationType
{
    Unknown = 0,
    MainWindow = 1,
    OtherWindow = 2,
    Service = 3,
    Explorer = 4,
    Console = 5,
    Critical = 1000
}

[Flags]
internal enum RestartManagerRebootReason : uint
{
    None = 0,
    PermissionDenied = 1,
    SessionMismatch = 2,
    CriticalProcess = 4,
    CriticalService = 8,
    DetectedSelf = 16
}

internal sealed record RestartManagerBlocker(
    int ProcessId,
    string ApplicationName,
    string ServiceShortName,
    RestartManagerApplicationType ApplicationType,
    uint ApplicationStatus,
    bool Restartable,
    RestartManagerRebootReason RebootReason,
    string ExecutablePath,
    IReadOnlyList<string> EvidenceResources);

internal sealed record RestartManagerQueryResult(
    bool Available,
    bool Succeeded,
    IReadOnlyList<string> Resources,
    IReadOnlyList<RestartManagerBlocker> Blockers,
    RestartManagerRebootReason RebootReason,
    int ErrorCode,
    string Diagnostics);

internal interface IWindowsRestartManagerService
{
    RestartManagerQueryResult Query(IReadOnlyCollection<string> resources);
}

internal sealed class WindowsRestartManagerService : IWindowsRestartManagerService
{
    private const int ErrorSuccess = 0;
    private const int ErrorMoreData = 234;
    private const int SessionKeyLength = 32;
    private const int RegistrationBatchSize = 64;
    private const int MaximumListAttempts = 5;

    public RestartManagerQueryResult Query(IReadOnlyCollection<string> resources)
    {
        var registeredResources = NormalizeResources(resources);
        if (!OperatingSystem.IsWindows())
        {
            return Failure(
                available: false,
                registeredResources,
                errorCode: 0,
                "Windows Restart Manager non è disponibile su questo sistema operativo.");
        }

        if (registeredResources.Count == 0)
        {
            return Failure(
                available: true,
                registeredResources,
                errorCode: 0,
                "Nessuna risorsa installata valida da registrare con Restart Manager.");
        }

        uint sessionHandle = 0;
        var sessionStarted = false;
        try
        {
            var sessionKey = new StringBuilder(SessionKeyLength + 1);
            var error = NativeMethods.RmStartSession(out sessionHandle, 0, sessionKey);
            if (error != ErrorSuccess)
            {
                return Failure(
                    available: true,
                    registeredResources,
                    error,
                    FormatWin32Error("RmStartSession", error));
            }
            sessionStarted = true;

            foreach (var batch in registeredResources.Chunk(RegistrationBatchSize))
            {
                var files = batch.ToArray();
                error = NativeMethods.RmRegisterResources(
                    sessionHandle,
                    (uint)files.Length,
                    files,
                    0,
                    null,
                    0,
                    null);
                if (error != ErrorSuccess)
                {
                    return Failure(
                        available: true,
                        registeredResources,
                        error,
                        FormatWin32Error("RmRegisterResources", error));
                }
            }

            return ReadBlockers(sessionHandle, registeredResources);
        }
        catch (DllNotFoundException ex)
        {
            return Failure(false, registeredResources, ex.HResult, ex.Message);
        }
        catch (EntryPointNotFoundException ex)
        {
            return Failure(false, registeredResources, ex.HResult, ex.Message);
        }
        catch (Exception ex)
        {
            return Failure(true, registeredResources, ex.HResult, ex.Message);
        }
        finally
        {
            if (sessionStarted)
            {
                var endError = NativeMethods.RmEndSession(sessionHandle);
                if (endError != ErrorSuccess)
                {
                    LogService.WriteEvent(
                        "winget-recovery", "restart-manager-session", "cleanup-failure",
                        "", endError, FormatWin32Error("RmEndSession", endError));
                }
            }
        }
    }

    private static RestartManagerQueryResult ReadBlockers(
        uint sessionHandle,
        IReadOnlyList<string> resources)
    {
        uint required = 0;
        uint count = 0;
        uint rebootReasons = 0;
        var error = NativeMethods.RmGetList(
            sessionHandle,
            out required,
            ref count,
            null,
            ref rebootReasons);
        if (error == ErrorSuccess)
            return Success(resources, [], (RestartManagerRebootReason)rebootReasons);
        if (error != ErrorMoreData)
            return Failure(true, resources, error, FormatWin32Error("RmGetList", error));

        for (var attempt = 0; attempt < MaximumListAttempts; attempt++)
        {
            if (required == 0)
                return Success(resources, [], (RestartManagerRebootReason)rebootReasons);

            var processInfo = new RestartManagerProcessInfo[required];
            count = required;
            error = NativeMethods.RmGetList(
                sessionHandle,
                out required,
                ref count,
                processInfo,
                ref rebootReasons);
            if (error == ErrorMoreData)
                continue;
            if (error != ErrorSuccess)
                return Failure(true, resources, error, FormatWin32Error("RmGetList", error));

            var reason = (RestartManagerRebootReason)rebootReasons;
            var blockers = processInfo
                .Take(checked((int)count))
                .Select(info => ToBlocker(info, reason, resources))
                .DistinctBy(blocker => (blocker.ProcessId, blocker.ServiceShortName))
                .OrderBy(blocker => blocker.ApplicationName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(blocker => blocker.ProcessId)
                .ToList();
            return Success(resources, blockers, reason);
        }

        return Failure(
            true,
            resources,
            ErrorMoreData,
            "RmGetList ha continuato a restituire ERROR_MORE_DATA durante una lista di processi in cambiamento.");
    }

    private static RestartManagerBlocker ToBlocker(
        RestartManagerProcessInfo info,
        RestartManagerRebootReason rebootReason,
        IReadOnlyList<string> evidenceResources)
    {
        var processId = unchecked((int)info.Process.ProcessId);
        return new RestartManagerBlocker(
            processId,
            info.ApplicationName?.Trim() ?? "",
            info.ServiceShortName?.Trim() ?? "",
            info.ApplicationType,
            info.ApplicationStatus,
            info.Restartable,
            rebootReason,
            TryResolveExecutablePath(processId),
            evidenceResources);
    }

    private static string TryResolveExecutablePath(int processId)
    {
        if (processId <= 0)
            return "";
        try
        {
            using var process = Process.GetProcessById(processId);
            return Path.GetFullPath(process.MainModule?.FileName ?? "");
        }
        catch
        {
            return "";
        }
    }

    private static List<string> NormalizeResources(IEnumerable<string> resources)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in resources)
        {
            if (string.IsNullOrWhiteSpace(resource) || !Path.IsPathFullyQualified(resource))
                continue;
            try
            {
                var fullPath = Path.GetFullPath(resource);
                if (File.Exists(fullPath))
                    normalized.Add(fullPath);
            }
            catch
            {
            }
        }
        return normalized.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static RestartManagerQueryResult Success(
        IReadOnlyList<string> resources,
        IReadOnlyList<RestartManagerBlocker> blockers,
        RestartManagerRebootReason rebootReason)
    {
        var diagnostics = $"Risorse registrate ({resources.Count}): {string.Join("; ", resources)}" +
                          Environment.NewLine +
                          $"Blocker Restart Manager ({blockers.Count}): " +
                          (blockers.Count == 0
                              ? "nessuno"
                              : string.Join("; ", blockers.Select(Describe))) +
                          Environment.NewLine + $"RebootReason={rebootReason}.";
        return new RestartManagerQueryResult(
            true, true, resources, blockers, rebootReason, ErrorSuccess, diagnostics);
    }

    private static RestartManagerQueryResult Failure(
        bool available,
        IReadOnlyList<string> resources,
        int errorCode,
        string diagnostics) =>
        new(
            available,
            false,
            resources,
            [],
            RestartManagerRebootReason.None,
            errorCode,
            $"Risorse registrabili ({resources.Count}): {string.Join("; ", resources)}" +
            Environment.NewLine + diagnostics);

    internal static string Describe(RestartManagerBlocker blocker) =>
        $"{blocker.ApplicationName} (PID {blocker.ProcessId}, tipo={blocker.ApplicationType}, " +
        $"servizio={blocker.ServiceShortName}, restartable={blocker.Restartable}, " +
        $"status=0x{blocker.ApplicationStatus:X}, reboot={blocker.RebootReason}, " +
        $"path={blocker.ExecutablePath}, evidence={string.Join("; ", blocker.EvidenceResources)})";

    private static string FormatWin32Error(string operation, int error) =>
        $"{operation} non riuscito: Win32={error} ({new Win32Exception(error).Message}).";

    [StructLayout(LayoutKind.Sequential)]
    private struct RestartManagerUniqueProcess
    {
        public uint ProcessId;
        public FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RestartManagerProcessInfo
    {
        public RestartManagerUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ApplicationName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string ServiceShortName;

        public RestartManagerApplicationType ApplicationType;
        public uint ApplicationStatus;
        public uint TerminalServicesSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Restartable;
    }

    private static class NativeMethods
    {
        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        internal static extern int RmStartSession(
            out uint sessionHandle,
            int sessionFlags,
            StringBuilder sessionKey);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        internal static extern int RmRegisterResources(
            uint sessionHandle,
            uint fileCount,
            string[]? fileNames,
            uint applicationCount,
            RestartManagerUniqueProcess[]? applications,
            uint serviceCount,
            string[]? serviceNames);

        [DllImport("rstrtmgr.dll")]
        internal static extern int RmGetList(
            uint sessionHandle,
            out uint processInfoNeeded,
            ref uint processInfoCount,
            [In, Out] RestartManagerProcessInfo[]? affectedApplications,
            ref uint rebootReasons);

        [DllImport("rstrtmgr.dll")]
        internal static extern int RmEndSession(uint sessionHandle);
    }
}
