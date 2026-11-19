using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace UpdateCenter.ConsentTestHost;

internal static class SafetyGuard
{
    // Check the compiled host, including linked sources and compiler-generated lambdas.
    // This is a regression gate, not a sandbox against a malicious replacement executable.
    internal static void Verify()
    {
        var assembly = typeof(SafetyGuard).Assembly;
        if (assembly.GetReferencedAssemblies().Any(a => a.Name!.StartsWith("UpdateCenter", StringComparison.Ordinal)))
            throw new InvalidOperationException("Production/provider assembly reference is forbidden.");
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => unchecked((ushort)o.Value));
        foreach (var type in assembly.GetTypes())
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                     BindingFlags.Instance | BindingFlags.DeclaredOnly).Cast<MethodBase>()
                     .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)))
        {
            if (method.GetCustomAttribute<DllImportAttribute>() is { } import &&
                !(type == typeof(UpdateCenter.Services.WpfRunnerConsent) && import.Value == "wtsapi32.dll" &&
                  method.Name is "WTSQuerySessionInformation" or "WTSFreeMemory"))
                throw new InvalidOperationException("Native import forbidden: " + method);
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il is null) continue;
            for (var i = 0; i < il.Length;)
            {
                ushort value = il[i++];
                if (value == 0xfe) value = (ushort)(0xfe00 | il[i++]);
                var op = codes[value];
                if (op == OpCodes.Calli) throw new InvalidOperationException("Indirect native calls are forbidden.");
                if (op.OperandType == OperandType.InlineMethod || op.OperandType == OperandType.InlineTok)
                {
                    var member = method.Module.ResolveMember(BitConverter.ToInt32(il, i),
                        type.IsGenericType ? type.GetGenericArguments() : null,
                        method.IsGenericMethod ? method.GetGenericArguments() : null);
                    if (member is MethodBase called && IsForbidden(called))
                        throw new InvalidOperationException($"Forbidden call in {method}: {called}");
                }
                i += op.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                    _ => 4
                };
            }
        }
    }

    internal static bool IsForbidden(MethodBase method)
    {
        var type = method.DeclaringType?.FullName ?? "";
        if (type == "System.Diagnostics.Process")
            return method.Name is not ("GetCurrentProcess" or "GetProcessById" or "get_SessionId" or "get_Id" or "get_HasExited" or "Dispose");
        if (type.StartsWith("System.Net.", StringComparison.Ordinal) ||
            type.StartsWith("System.Management.", StringComparison.Ordinal) ||
            type.StartsWith("System.ServiceProcess.", StringComparison.Ordinal) ||
            type.StartsWith("Microsoft.Win32.Registry", StringComparison.Ordinal) ||
            type == "System.Runtime.InteropServices.NativeLibrary" ||
            type.StartsWith("System.Runtime.Loader.", StringComparison.Ordinal)) return true;
        return (type == "System.Type" && method.Name is "GetTypeFromProgID" or "GetTypeFromCLSID") ||
               (type == "System.Activator" && method.Name.StartsWith("CreateInstance", StringComparison.Ordinal)) ||
               (type == "System.Reflection.Assembly" && method.Name.StartsWith("Load", StringComparison.Ordinal)) ||
               (type.StartsWith("System.Reflection.", StringComparison.Ordinal) && method.Name == "Invoke") ||
               (type == "System.Runtime.InteropServices.Marshal" && method.Name.StartsWith("GetDelegateForFunctionPointer", StringComparison.Ordinal));
    }
}
