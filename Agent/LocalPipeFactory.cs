using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using UpdateCenter.Contracts;

namespace UpdateCenter.Agent;

internal static class LocalPipeFactory
{
    public static NamedPipeServerStream CreateControlPipe(
        string pipeName,
        int maximumInstances,
        bool isWindowsService)
    {
        if (!isWindowsService)
        {
            return new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maximumInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.WriteThrough);
        }

        var security = CreateBaseSecurity();
        AddReadWriteRule(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        return Create(pipeName, maximumInstances, security);
    }

    public static NamedPipeServerStream CreateHelperPipe(string pipeName, SecurityIdentifier userSid)
    {
        var security = CreateBaseSecurity();
        AddReadWriteRule(security, userSid);
        return Create(pipeName, 1, security);
    }

    public static NamedPipeServerStream CreateApprovalPipe(int maximumInstances)
    {
        var security = CreateBaseSecurity();
        AddReadWriteRule(security, new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null));
        return Create(AgentProtocol.ApprovalPipeName, maximumInstances, security);
    }

    private static PipeSecurity CreateBaseSecurity()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        AddReadWriteRule(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        return security;
    }

    private static void AddReadWriteRule(PipeSecurity security, SecurityIdentifier sid) =>
        security.AddAccessRule(new PipeAccessRule(
            sid,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

    private static NamedPipeServerStream Create(string pipeName, int maximumInstances, PipeSecurity security) =>
        NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maximumInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
}
