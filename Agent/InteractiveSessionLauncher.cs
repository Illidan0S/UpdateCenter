using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace UpdateCenter.Agent;

internal sealed class InteractiveSessionLauncher : IDisposable
{
    private const uint InvalidSessionId = 0xFFFFFFFF;
    private const uint TokenAllAccess = 0x000F01FF;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateNoWindow = 0x08000000;
    private readonly SafeAccessTokenHandle? _primaryToken;
    private readonly bool _serviceMode;

    private InteractiveSessionLauncher(
        bool serviceMode,
        SafeAccessTokenHandle? primaryToken,
        SecurityIdentifier userSid)
    {
        _serviceMode = serviceMode;
        _primaryToken = primaryToken;
        UserSid = userSid;
    }

    public SecurityIdentifier UserSid { get; }

    public static InteractiveSessionLauncher Prepare(bool serviceMode)
    {
        if (!serviceMode)
        {
            var currentSid = WindowsIdentity.GetCurrent().User
                             ?? throw new InvalidOperationException("Identità dell'utente corrente non disponibile.");
            return new InteractiveSessionLauncher(false, null, currentSid);
        }

        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == InvalidSessionId)
            throw new InvalidOperationException("Nessuna sessione utente interattiva disponibile.");
        if (!WTSQueryUserToken(sessionId, out var impersonationToken))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Impossibile ottenere il token della sessione utente.");

        using var sourceToken = new SafeAccessTokenHandle(impersonationToken);
        if (!DuplicateTokenEx(
                sourceToken,
                TokenAllAccess,
                IntPtr.Zero,
                SecurityImpersonation,
                TokenPrimary,
                out var primaryToken))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Impossibile creare il token primario dell'utente.");

        try
        {
            using var identity = new WindowsIdentity(primaryToken.DangerousGetHandle());
            var sid = identity.User
                      ?? throw new InvalidOperationException("SID della sessione utente non disponibile.");
            return new InteractiveSessionLauncher(true, primaryToken, sid);
        }
        catch
        {
            primaryToken.Dispose();
            throw;
        }
    }

    public Process Start(string executable, IReadOnlyList<string> arguments, string workingDirectory)
    {
        if (!_serviceMode)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            return Process.Start(startInfo)
                   ?? throw new InvalidOperationException("Impossibile avviare il Session Helper.");
        }

        if (_primaryToken is null || _primaryToken.IsInvalid)
            throw new InvalidOperationException("Token della sessione utente non disponibile.");

        IntPtr environment = IntPtr.Zero;
        try
        {
            if (!CreateEnvironmentBlock(out environment, _primaryToken, false))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Impossibile creare l'ambiente della sessione utente.");

            var startup = new StartupInfo
            {
                Size = Marshal.SizeOf<StartupInfo>(),
                Desktop = @"winsta0\default"
            };
            var commandLine = new StringBuilder(BuildCommandLine(executable, arguments));
            if (!CreateProcessAsUser(
                    _primaryToken,
                    executable,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CreateUnicodeEnvironment | CreateNoWindow,
                    environment,
                    workingDirectory,
                    ref startup,
                    out var processInformation))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Impossibile avviare il Session Helper nella sessione utente.");

            try
            {
                return Process.GetProcessById(unchecked((int)processInformation.ProcessId));
            }
            finally
            {
                CloseHandle(processInformation.Thread);
                CloseHandle(processInformation.Process);
            }
        }
        finally
        {
            if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
        }
    }

    private static string BuildCommandLine(string executable, IReadOnlyList<string> arguments) =>
        string.Join(" ", new[] { Quote(executable) }.Concat(arguments.Select(Quote)));

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    public void Dispose() => _primaryToken?.Dispose();

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        SafeAccessTokenHandle existingToken,
        uint desiredAccess,
        IntPtr tokenAttributes,
        int impersonationLevel,
        int tokenType,
        out SafeAccessTokenHandle newToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(
        out IntPtr environment,
        SafeAccessTokenHandle token,
        bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessAsUser(
        SafeAccessTokenHandle token,
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }
}
