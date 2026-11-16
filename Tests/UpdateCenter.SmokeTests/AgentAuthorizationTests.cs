using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using UpdateCenter.Agent;
using UpdateCenter.Contracts;
using UpdateCenter.Core;

internal static class AgentAuthorizationTests
{
    internal static async Task RunAsync(string root)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[SignedRequestProtocol.ControllerHeader] = new string('A', 64);
        context.Request.Headers[SignedRequestProtocol.TimestampHeader] = long.MaxValue.ToString();
        context.Request.Headers[SignedRequestProtocol.NonceHeader] = Guid.NewGuid().ToString();
        context.Request.Headers[SignedRequestProtocol.SignatureHeader] = "AA==";
        // Invalid timestamps must be rejected before consulting an identity store.
        if (await new SignedRequestVerifier(null!).VerifyAsync(context.Request, default))
            throw new InvalidOperationException("Out-of-range timestamp accepted.");
        Console.WriteLine("PASS Agent rejects out-of-range timestamps without throwing.");
        var agentRoot = Path.Combine(root, "agent-authorization");
        Directory.CreateDirectory(agentRoot);
        var settingsPath = Path.Combine(agentRoot, "network-settings.json");
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(new
        {
            enabled = true, agentId = Guid.NewGuid(), displayName = "Fixture",
            connectionRequestsConfigured = true, connectionRequestsAllowed = true
        }));
        var store = new AgentNetworkSettingsStore(agentRoot);
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=fixture-controller", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = certificateRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        var fingerprint = AgentNetworkSettingsStore.CertificateFingerprint(certificate);
        store.PairController("Fixture", certificate);
        var verifier = new SignedRequestVerifier(store);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid();
        Check(await verifier.VerifyAsync(Signed(certificate, now, nonce).Request, default), "Valid signed request rejected.");
        Check(!await verifier.VerifyAsync(Signed(certificate, now, nonce).Request, default), "Replay accepted.");
        var alteredBody = Signed(certificate, now, Guid.NewGuid());
        alteredBody.Request.Body = new MemoryStream("tampered"u8.ToArray());
        Check(!await verifier.VerifyAsync(alteredBody.Request, default), "Changed body accepted.");
        var alteredPath = Signed(certificate, now, Guid.NewGuid());
        alteredPath.Request.Path = "/api/v1/updates";
        Check(!await verifier.VerifyAsync(alteredPath.Request, default), "Changed path accepted.");
        Check(!await verifier.VerifyAsync(Signed(certificate,
            DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds().ToString(), Guid.NewGuid()).Request, default),
            "Expired request accepted.");
        Check(!await verifier.VerifyAsync(new DefaultHttpContext().Request, default), "Unsigned request accepted.");
        using var otherKey = RSA.Create(2048);
        var otherRequest = new CertificateRequest("CN=untrusted-fixture", otherKey,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var otherCertificate = otherRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        Check(!await verifier.VerifyAsync(Signed(otherCertificate, now, Guid.NewGuid()).Request, default), "Other key accepted.");
        var reopened = new AgentNetworkSettingsStore(agentRoot);
        using var restored = reopened.GetControllerCertificate();
        Check(restored is not null && fingerprint == AgentNetworkSettingsStore.CertificateFingerprint(restored),
            "Persisted DER identity changed.");
        store.RevokeController();
        Check(!await verifier.VerifyAsync(Signed(certificate, now, Guid.NewGuid()).Request, default), "Revoked controller accepted.");
        Console.WriteLine("PASS Agent signatures, replay, expiry, body/path integrity, revocation and DER reopening.");

        using var identity = WindowsIdentity.GetCurrent();
        var owner = new ApprovalIdentity(identity.User!, (uint)Process.GetCurrentProcess().SessionId);
        var manager = new ConnectionRequestManager(NullLogger<ConnectionRequestManager>.Instance, store,
            () => owner, (_, _) => { });
        var pending = manager.Create(new ConnectionRequestCreate
        {
            ControllerId = new Guid(SHA256.HashData(certificate.RawData).AsSpan(0, 16)),
            ControllerName = "Fixture", ControllerCertificateBase64 = Convert.ToBase64String(certificate.RawData)
        }, "127.0.0.1");
        Check(pending.Success, "Approval fixture was not created.");
        var otherSession = owner with { SessionId = owner.SessionId + 1 };
        var otherUser = owner with { UserSid = new SecurityIdentifier(WellKnownSidType.AnonymousSid, null) };
        foreach (var denied in new[] { otherSession, otherUser })
        {
            Check(manager.GetPending(denied).Count == 0, "Another user/session could list the request.");
            Check(!manager.Decide(pending.RequestId, true, denied, out _), "Another user/session approved the request.");
            Check(!store.GetConfiguration().HasController, "Rejected approval changed the controller.");
        }
        Check(manager.GetPending(owner).Count == 1 && manager.Decide(pending.RequestId, true, owner, out _),
            "The assigned session could not approve its request.");
        store.RevokeController();
        var rejectedPfx = manager.Create(new ConnectionRequestCreate
        {
            ControllerId = new Guid(SHA256.HashData(certificate.RawData).AsSpan(0, 16)),
            ControllerName = "Invalid PFX", ControllerCertificateBase64 = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx))
        }, "127.0.0.2");
        Check(!rejectedPfx.Success && rejectedPfx.ErrorCode == "InvalidControllerCertificate",
            "A PFX was accepted at the public DER certificate boundary.");
        Console.WriteLine("PASS approval is bound to the assigned Windows user and session.");

        AssertPrivateAcl(new DirectoryInfo(agentRoot).GetAccessControl(), requireProtection: true);
        AssertPrivateAcl(new FileInfo(settingsPath).GetAccessControl(), requireProtection: false);
        var trustedOwners = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null)
        };
        foreach (var trusted in trustedOwners)
        {
            var descriptor = new DirectorySecurity();
            descriptor.SetOwner(trusted);
            AgentSecretDirectory.AssertTrustedOwner(descriptor, trustedOwners);
        }
        // An owner can restore its own access even after a private DACL is applied.
        var untrustedDescriptor = new DirectorySecurity();
        untrustedDescriptor.SetOwner(new SecurityIdentifier(WellKnownSidType.AnonymousSid, null));
        var untrustedOwnerRejected = false;
        try { AgentSecretDirectory.AssertTrustedOwner(untrustedDescriptor, trustedOwners); }
        catch (UnauthorizedAccessException) { untrustedOwnerRejected = true; }
        Check(untrustedOwnerRejected, "A pre-existing untrusted owner was accepted for Agent secrets.");
        var corruptRoot = Path.Combine(root, "agent-corrupt-config");
        Directory.CreateDirectory(corruptRoot);
        var corruptPath = Path.Combine(corruptRoot, "network-settings.json");
        File.WriteAllText(corruptPath, "{");
        try { _ = new AgentNetworkSettingsStore(corruptRoot); throw new InvalidOperationException("Corrupt identity reset silently."); }
        catch (JsonException) { Check(File.ReadAllText(corruptPath) == "{", "Corrupt configuration was overwritten."); }
        Console.WriteLine("PASS Agent secret ACLs and preservation of unreadable settings.");
        await VerifyPipeAsync(owner);
    }

    private static DefaultHttpContext Signed(X509Certificate2 certificate, string timestamp, Guid nonce)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/scans";
        var body = "{}"u8.ToArray();
        context.Request.Body = new MemoryStream(body);
        var nonceText = nonce.ToString("D");
        context.Request.Headers[SignedRequestProtocol.ControllerHeader] = AgentNetworkSettingsStore.CertificateFingerprint(certificate);
        context.Request.Headers[SignedRequestProtocol.TimestampHeader] = timestamp;
        context.Request.Headers[SignedRequestProtocol.NonceHeader] = nonceText;
        using var key = certificate.GetRSAPrivateKey()!;
        context.Request.Headers[SignedRequestProtocol.SignatureHeader] = Convert.ToBase64String(key.SignData(
            Encoding.UTF8.GetBytes(SignedRequestProtocol.BuildCanonical("POST", "/api/v1/scans", timestamp, nonceText, body)),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return context;
    }

    private static void AssertPrivateAcl(FileSystemSecurity security, bool requireProtection)
    {
        Check(!requireProtection || security.AreAccessRulesProtected, "Secret directory inherits public rules.");
        using var identity = WindowsIdentity.GetCurrent();
        var allowed = new HashSet<SecurityIdentifier>
        {
            identity.User!,
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null)
        };
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            Check(rule.AccessControlType != AccessControlType.Allow || allowed.Contains((SecurityIdentifier)rule.IdentityReference),
                "Secret data readable by another principal: " + rule.IdentityReference);
    }

    private static async Task VerifyPipeAsync(ApprovalIdentity owner)
    {
        var name = "UpdateCenter-fixture-" + Guid.NewGuid().ToString("N");
        await using var pipe = LocalPipeFactory.CreateApprovalPipe(1, name);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        foreach (PipeAccessRule rule in pipe.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)))
            Check(!rule.IdentityReference.Equals(users) || (rule.PipeAccessRights & PipeAccessRights.CreateNewInstance) == 0,
                "Ordinary approval clients can create server instances.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        async Task ServeAsync()
        {
            await pipe.WaitForConnectionAsync(timeout.Token);
            var request = await PipeJsonProtocol.ReadAsync<AgentRequest>(pipe, timeout.Token);
            Check(ApprovalIdentity.FromPipe(pipe) == owner, "Kernel pipe identity did not match the client user/session.");
            await PipeJsonProtocol.WriteAsync(pipe, new AgentResponse { RequestId = request.RequestId, Success = true }, timeout.Token);
        }
        var server = ServeAsync();
        try
        {
            var response = await new AgentLocalClient(name).SendAsync(new AgentRequest { Command = AgentCommands.GetStatus },
                TimeSpan.FromSeconds(5), timeout.Token);
            Check(response.Success, "Approval pipe client failed.");
            await server;
        }
        finally
        {
            timeout.Cancel();
            try { await server; } catch (OperationCanceledException) { }
        }
        Console.WriteLine("PASS real named-pipe client identification and restricted client rights.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
