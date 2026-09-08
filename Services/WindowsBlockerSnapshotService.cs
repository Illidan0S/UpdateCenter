using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace UpdateCenter.Services;

internal sealed record WindowsServiceProcessSnapshot(
    string ServiceName,
    string DisplayName,
    int ProcessId,
    uint ServiceType,
    uint CurrentState,
    string ExecutablePath,
    int ServicesInProcess,
    uint ServiceFlags = 0)
{
    public bool IsRunning => CurrentState == WindowsServiceControl.ServiceRunning;
    public bool IsDedicated =>
        (ServiceType & WindowsServiceControl.ServiceWin32OwnProcess) != 0 && ServicesInProcess == 1;
    public bool IsShared =>
        (ServiceType & WindowsServiceControl.ServiceWin32ShareProcess) != 0 || ServicesInProcess != 1;
    public bool RunsInSystemProcess => (ServiceFlags & WindowsServiceControl.ServiceRunsInSystemProcess) != 0;
}

internal sealed record RestartManagerParentSnapshot(
    int ProcessId,
    int ParentProcessId,
    string ExecutablePath,
    IReadOnlyList<WindowsServiceProcessSnapshot> Services);

internal sealed record WindowsBlockerNativeSnapshot(
    string ExecutablePath,
    int ParentProcessId,
    IReadOnlyList<RestartManagerParentSnapshot> ParentChain,
    IReadOnlyList<WindowsServiceProcessSnapshot> Services);

internal interface IWindowsBlockerSnapshotService
{
    WindowsBlockerNativeSnapshot Capture(int processId, string restartManagerServiceName);
}

internal sealed class WindowsBlockerSnapshotService : IWindowsBlockerSnapshotService
{
    private const int MaximumParentDepth = 3;

    public WindowsBlockerNativeSnapshot Capture(int processId, string restartManagerServiceName)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0)
            return new WindowsBlockerNativeSnapshot("", 0, [], []);

        var processPath = NativeProcessSnapshot.TryGetExecutablePath(processId);
        var processes = NativeProcessSnapshot.Read();
        var services = WindowsServiceControl.EnumerateServices();
        var serviceCounts = services
            .Where(service => service.ProcessId > 0)
            .GroupBy(service => service.ProcessId)
            .ToDictionary(group => group.Key, group => group.Count());
        var directServices = MapServices(processId, processPath, services, serviceCounts).ToList();

        if (!string.IsNullOrWhiteSpace(restartManagerServiceName))
        {
            var restartManagerService = services.FirstOrDefault(service =>
                service.ServiceName.Equals(restartManagerServiceName, StringComparison.OrdinalIgnoreCase));
            if (restartManagerService is not null &&
                directServices.All(service => !service.ServiceName.Equals(
                    restartManagerService.ServiceName, StringComparison.OrdinalIgnoreCase)))
            {
                directServices.Add(ToSnapshot(
                    restartManagerService,
                    NativeProcessSnapshot.TryGetExecutablePath(restartManagerService.ProcessId),
                    serviceCounts));
            }
        }

        var parents = new List<RestartManagerParentSnapshot>();
        var seen = new HashSet<int> { processId };
        var current = processId;
        var immediateParent = processes.TryGetValue(processId, out var process) ? process.ParentProcessId : 0;
        for (var depth = 0; depth < MaximumParentDepth; depth++)
        {
            if (!processes.TryGetValue(current, out var currentProcess) ||
                currentProcess.ParentProcessId <= 0 || !seen.Add(currentProcess.ParentProcessId))
                break;

            var parentId = currentProcess.ParentProcessId;
            var parentPath = NativeProcessSnapshot.TryGetExecutablePath(parentId);
            var parentServices = MapServices(parentId, parentPath, services, serviceCounts).ToList();
            var parentParentId = processes.TryGetValue(parentId, out var parent)
                ? parent.ParentProcessId
                : 0;
            parents.Add(new RestartManagerParentSnapshot(
                parentId, parentParentId, parentPath, parentServices));
            current = parentId;
        }

        return new WindowsBlockerNativeSnapshot(
            processPath, immediateParent, parents, directServices);
    }

    private static IEnumerable<WindowsServiceProcessSnapshot> MapServices(
        int processId,
        string executablePath,
        IReadOnlyList<WindowsServiceControl.ServiceEntry> services,
        IReadOnlyDictionary<int, int> serviceCounts) =>
        services
            .Where(service => service.ProcessId == processId)
            .Select(service => ToSnapshot(service, executablePath, serviceCounts));

    private static WindowsServiceProcessSnapshot ToSnapshot(
        WindowsServiceControl.ServiceEntry service,
        string executablePath,
        IReadOnlyDictionary<int, int> serviceCounts) =>
        new(
            service.ServiceName,
            service.DisplayName,
            service.ProcessId,
            service.ServiceType,
            service.CurrentState,
            executablePath,
            serviceCounts.TryGetValue(service.ProcessId, out var count) ? count : 0,
            service.ServiceFlags);
}

internal sealed record WindowsServiceControlResult(bool Succeeded, string Diagnostics);

internal interface IWindowsServiceControl
{
    WindowsServiceProcessSnapshot? Query(string serviceName);
    WindowsServiceControlResult StopAndWait(string serviceName, TimeSpan timeout);
    WindowsServiceControlResult StartAndWait(string serviceName, TimeSpan timeout);
}

internal sealed class WindowsServiceControl : IWindowsServiceControl
{
    internal const uint ServiceWin32OwnProcess = 0x00000010;
    internal const uint ServiceWin32ShareProcess = 0x00000020;
    internal const uint ServiceStopped = 0x00000001;
    internal const uint ServiceRunning = 0x00000004;
    internal const uint ServiceRunsInSystemProcess = 0x00000001;
    private const uint ScManagerConnect = 0x0001;
    private const uint ScManagerEnumerateService = 0x0004;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceStop = 0x0020;
    private const uint ServiceControlStop = 0x00000001;
    private const int ScEnumProcessInfo = 0;
    private const uint ServiceWin32 = ServiceWin32OwnProcess | ServiceWin32ShareProcess;
    private const uint ServiceStateAll = 0x00000003;
    private const int ErrorMoreData = 234;
    private const int ErrorServiceAlreadyRunning = 1056;
    private const int ErrorServiceNotActive = 1062;

    public WindowsServiceProcessSnapshot? Query(string serviceName)
    {
        var services = EnumerateServices();
        var entry = services.FirstOrDefault(service =>
            service.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return null;
        var sameProcessCount = entry.ProcessId <= 0
            ? 0
            : services.Count(service => service.ProcessId == entry.ProcessId);
        return new WindowsServiceProcessSnapshot(
            entry.ServiceName,
            entry.DisplayName,
            entry.ProcessId,
            entry.ServiceType,
            entry.CurrentState,
            NativeProcessSnapshot.TryGetExecutablePath(entry.ProcessId),
            sameProcessCount,
            entry.ServiceFlags);
    }

    public WindowsServiceControlResult StopAndWait(string serviceName, TimeSpan timeout) =>
        ChangeState(serviceName, start: false, timeout);

    public WindowsServiceControlResult StartAndWait(string serviceName, TimeSpan timeout) =>
        ChangeState(serviceName, start: true, timeout);

    private static WindowsServiceControlResult ChangeState(
        string serviceName,
        bool start,
        TimeSpan timeout)
    {
        if (!OperatingSystem.IsWindows())
            return new WindowsServiceControlResult(false, "Service Control Manager non disponibile.");

        using var manager = NativeMethods.OpenSCManager(null, null, ScManagerConnect);
        if (manager.IsInvalid)
            return Win32Failure("OpenSCManager");
        var access = ServiceQueryStatus | (start ? ServiceStart : ServiceStop);
        using var service = NativeMethods.OpenService(manager, serviceName, access);
        if (service.IsInvalid)
            return Win32Failure($"OpenService({serviceName})");

        if (!TryQueryState(service, out var currentState, out var queryError))
            return new WindowsServiceControlResult(false, queryError);
        var desiredState = start ? ServiceRunning : ServiceStopped;
        if (currentState == desiredState)
            return new WindowsServiceControlResult(true, $"Servizio {serviceName} già nello stato richiesto.");

        bool requested;
        int error;
        if (start)
        {
            requested = NativeMethods.StartService(service, 0, null);
            error = requested ? 0 : Marshal.GetLastWin32Error();
            if (!requested && error != ErrorServiceAlreadyRunning)
                return Win32Failure($"StartService({serviceName})", error);
        }
        else
        {
            requested = NativeMethods.ControlService(
                service, ServiceControlStop, out _);
            error = requested ? 0 : Marshal.GetLastWin32Error();
            if (!requested && error != ErrorServiceNotActive)
                return Win32Failure($"ControlService(STOP, {serviceName})", error);
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!TryQueryState(service, out currentState, out queryError))
                return new WindowsServiceControlResult(false, queryError);
            if (currentState == desiredState)
                return new WindowsServiceControlResult(true,
                    $"Servizio {serviceName} nello stato {(start ? "Running" : "Stopped")}.");
            Thread.Sleep(100);
        }
        return new WindowsServiceControlResult(false,
            $"Timeout attendendo il servizio {serviceName} nello stato {(start ? "Running" : "Stopped")}.");
    }

    private static bool TryQueryState(
        NativeMethods.SafeServiceHandle service,
        out uint currentState,
        out string error)
    {
        var size = Marshal.SizeOf<ServiceStatusProcess>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!NativeMethods.QueryServiceStatusEx(service, 0, buffer, size, out _))
            {
                var code = Marshal.GetLastWin32Error();
                currentState = 0;
                error = $"QueryServiceStatusEx non riuscito: Win32={code} ({new Win32Exception(code).Message}).";
                return false;
            }
            currentState = Marshal.PtrToStructure<ServiceStatusProcess>(buffer).CurrentState;
            error = "";
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static IReadOnlyList<ServiceEntry> EnumerateServices()
    {
        if (!OperatingSystem.IsWindows())
            return [];
        using var manager = NativeMethods.OpenSCManager(
            null, null, ScManagerEnumerateService);
        if (manager.IsInvalid)
            return [];

        var resume = 0;
        _ = NativeMethods.EnumServicesStatusEx(
            manager, ScEnumProcessInfo, ServiceWin32, ServiceStateAll,
            IntPtr.Zero, 0, out var bytesNeeded, out _, ref resume, null);
        var error = Marshal.GetLastWin32Error();
        if (bytesNeeded <= 0 || error != ErrorMoreData)
            return [];

        var buffer = Marshal.AllocHGlobal(bytesNeeded);
        try
        {
            resume = 0;
            if (!NativeMethods.EnumServicesStatusEx(
                    manager, ScEnumProcessInfo, ServiceWin32, ServiceStateAll,
                    buffer, bytesNeeded, out _, out var returned, ref resume, null))
                return [];
            var size = Marshal.SizeOf<EnumServiceStatusProcess>();
            var result = new List<ServiceEntry>(returned);
            for (var index = 0; index < returned; index++)
            {
                var item = Marshal.PtrToStructure<EnumServiceStatusProcess>(buffer + index * size);
                result.Add(new ServiceEntry(
                    Marshal.PtrToStringUni(item.ServiceName) ?? "",
                    Marshal.PtrToStringUni(item.DisplayName) ?? "",
                    unchecked((int)item.Status.ProcessId),
                    item.Status.ServiceType,
                    item.Status.CurrentState,
                    item.Status.ServiceFlags));
            }
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static WindowsServiceControlResult Win32Failure(string operation, int? explicitError = null)
    {
        var error = explicitError ?? Marshal.GetLastWin32Error();
        return new WindowsServiceControlResult(
            false, $"{operation} non riuscito: Win32={error} ({new Win32Exception(error).Message}).");
    }

    internal sealed record ServiceEntry(
        string ServiceName,
        string DisplayName,
        int ProcessId,
        uint ServiceType,
        uint CurrentState,
        uint ServiceFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumServiceStatusProcess
    {
        public IntPtr ServiceName;
        public IntPtr DisplayName;
        public ServiceStatusProcess Status;
    }

    private static class NativeMethods
    {
        internal sealed class SafeServiceHandle : SafeHandle
        {
            private SafeServiceHandle() : base(IntPtr.Zero, true) { }
            public override bool IsInvalid => handle == IntPtr.Zero;
            protected override bool ReleaseHandle() => CloseServiceHandle(handle);
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeServiceHandle OpenSCManager(
            string? machineName, string? databaseName, uint desiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeServiceHandle OpenService(
            SafeServiceHandle manager, string serviceName, uint desiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumServicesStatusEx(
            SafeServiceHandle manager,
            int infoLevel,
            uint serviceType,
            uint serviceState,
            IntPtr services,
            int bufferSize,
            out int bytesNeeded,
            out int servicesReturned,
            ref int resumeHandle,
            string? groupName);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryServiceStatusEx(
            SafeServiceHandle service, int infoLevel, IntPtr buffer,
            int bufferSize, out int bytesNeeded);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ControlService(
            SafeServiceHandle service, uint control, out ServiceStatusProcess status);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool StartService(
            SafeServiceHandle service, int argumentCount, string[]? arguments);

        [DllImport("advapi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseServiceHandle(IntPtr serviceHandle);
    }
}

internal sealed class WindowsProcessLivenessService : IWindowsProcessLivenessService
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint StillActive = 259;
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidParameter = 87;
    private const int ErrorNotFound = 1168;

    public RestartManagerLivenessResult Check(int processId, long expectedStartTimeFileTime)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0 || expectedStartTimeFileTime <= 0)
            return Unknown(0, "PID o process start-time Restart Manager non valido.");

        using var process = NativeMethods.OpenProcess(
            ProcessQueryLimitedInformation, false, unchecked((uint)processId));
        if (process.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error is ErrorInvalidParameter or ErrorNotFound)
                return new RestartManagerLivenessResult(
                    RestartManagerProcessLiveness.DeadOrStale,
                    error,
                    $"OpenProcess conferma che PID {processId} non esiste più (Win32={error}).");
            return Unknown(error, error == ErrorAccessDenied
                ? $"Accesso negato verificando PID {processId}; liveness non determinabile."
                : $"OpenProcess PID {processId} non riuscito (Win32={error}); liveness non determinabile.");
        }

        if (!NativeMethods.GetExitCodeProcess(process, out var exitCode))
        {
            var error = Marshal.GetLastWin32Error();
            return Unknown(error, $"GetExitCodeProcess PID {processId} non riuscito (Win32={error}).");
        }
        if (exitCode != StillActive)
        {
            return new RestartManagerLivenessResult(
                RestartManagerProcessLiveness.DeadOrStale,
                0,
                $"PID {processId} è terminato (exitCode={exitCode}).");
        }

        if (!NativeMethods.GetProcessTimes(process, out var creation, out _, out _, out _))
        {
            var error = Marshal.GetLastWin32Error();
            return Unknown(error, $"GetProcessTimes PID {processId} non riuscito (Win32={error}).");
        }

        var actualStartTime = ((long)creation.dwHighDateTime << 32) | (uint)creation.dwLowDateTime;
        if (actualStartTime != expectedStartTimeFileTime)
        {
            return new RestartManagerLivenessResult(
                RestartManagerProcessLiveness.DeadOrStale,
                0,
                $"PID {processId} è stato riutilizzato: RM start={expectedStartTimeFileTime}, attuale={actualStartTime}.");
        }

        return new RestartManagerLivenessResult(
            RestartManagerProcessLiveness.Live,
            0,
            $"PID {processId} vivo con process start-time corrispondente al record RM.");
    }

    private static RestartManagerLivenessResult Unknown(int error, string diagnostics) =>
        new(RestartManagerProcessLiveness.Unknown, error, diagnostics);

    private static class NativeMethods
    {
        internal sealed class SafeProcessHandle : SafeHandle
        {
            private SafeProcessHandle() : base(IntPtr.Zero, true) { }
            public override bool IsInvalid => handle == IntPtr.Zero;
            protected override bool ReleaseHandle() => CloseHandle(handle);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeProcessHandle OpenProcess(
            uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessTimes(
            SafeProcessHandle process,
            out System.Runtime.InteropServices.ComTypes.FILETIME creationTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME exitTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME kernelTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}

internal static class NativeProcessSnapshot
{
    private const uint Th32csSnapProcess = 0x00000002;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    internal sealed record Entry(int ProcessId, int ParentProcessId);

    internal static IReadOnlyDictionary<int, Entry> Read()
    {
        if (!OperatingSystem.IsWindows())
            return new Dictionary<int, Entry>();
        using var snapshot = NativeMethods.CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot.IsInvalid)
            return new Dictionary<int, Entry>();
        var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
        var result = new Dictionary<int, Entry>();
        if (!NativeMethods.Process32First(snapshot, ref entry))
            return result;
        do
        {
            var processId = unchecked((int)entry.ProcessId);
            result[processId] = new Entry(processId, unchecked((int)entry.ParentProcessId));
            entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
        }
        while (NativeMethods.Process32Next(snapshot, ref entry));
        return result;
    }

    internal static string TryGetExecutablePath(int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0)
            return "";
        using var process = NativeMethods.OpenProcess(
            ProcessQueryLimitedInformation, false, unchecked((uint)processId));
        if (process.IsInvalid)
            return "";
        var capacity = 32768;
        var path = new StringBuilder(capacity);
        if (!NativeMethods.QueryFullProcessImageName(process, 0, path, ref capacity))
            return "";
        try { return Path.GetFullPath(path.ToString()); }
        catch { return path.ToString(); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    private static class NativeMethods
    {
        internal sealed class SafeSnapshotHandle : SafeHandle
        {
            private SafeSnapshotHandle() : base(new IntPtr(-1), true) { }
            public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);
            protected override bool ReleaseHandle() => CloseHandle(handle);
        }

        internal sealed class SafeProcessHandle : SafeHandle
        {
            private SafeProcessHandle() : base(IntPtr.Zero, true) { }
            public override bool IsInvalid => handle == IntPtr.Zero;
            protected override bool ReleaseHandle() => CloseHandle(handle);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeSnapshotHandle CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Process32First(SafeSnapshotHandle snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Process32Next(SafeSnapshotHandle snapshot, ref ProcessEntry32 entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeProcessHandle OpenProcess(
            uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryFullProcessImageName(
            SafeProcessHandle process, uint flags, StringBuilder executableName, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
