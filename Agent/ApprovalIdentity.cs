using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace UpdateCenter.Agent;

internal sealed record ApprovalIdentity(SecurityIdentifier UserSid, uint SessionId)
{
    internal static ApprovalIdentity FromPipe(NamedPipeServerStream pipe)
    {
        if (!GetNamedPipeClientSessionId(pipe.SafePipeHandle, out var sessionId))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        SecurityIdentifier? sid = null;
        pipe.RunAsClient(() =>
        {
            using var identity = WindowsIdentity.GetCurrent(ifImpersonating: true);
            sid = identity?.User;
        });
        return new ApprovalIdentity(sid ?? throw new UnauthorizedAccessException(), sessionId);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientSessionId(SafePipeHandle pipe, out uint clientSessionId);
}
