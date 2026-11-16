using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using UpdateCenter.Contracts;
using UpdateCenter.RemoteClient;

internal static class LanSecurityHardeningTests
{
    public static async Task RunAsync(string tempRoot)
    {
        var store = new ControllerIdentityStore(Path.Combine(tempRoot, "tls-controller"));
        var client = new RemoteAgentClient(store);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.UserKeySet);
        var pin = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.UseHttps(certificate)));
        await using var app = builder.Build();
        var received = 0;
        var id = Guid.NewGuid();
        app.MapPost("/api/v1/pair", () =>
        {
            Interlocked.Increment(ref received);
            return Results.Json(new PairingResponse { Success = true, AgentId = id, AgentCertificateSha256 = pin });
        });
        app.MapPost("/api/v1/connection-requests", () =>
        {
            Interlocked.Increment(ref received);
            return Results.Json(new ConnectionRequestResponse { Success = true, RequestId = Guid.NewGuid(),
                PollToken = "fixture", AgentId = id, AgentCertificateSha256 = pin });
        });
        await app.StartAsync();
        var port = new Uri(app.Urls.Single()).Port;
        var controlStore = new ControllerIdentityStore(Path.Combine(tempRoot, "tls-control"));
        var control = new RemoteAgentClient(controlStore);
        await control.PairAsync("127.0.0.1", port, "00000000", pin.ToLowerInvariant());
        if (received != 1) throw new InvalidOperationException("Loopback TLS control did not reach the endpoint.");
        await control.RequestConnectionAsync("127.0.0.1", port, pin);
        if (received != 2) throw new InvalidOperationException("Pinned connection request did not reach the endpoint.");
        using var originalIdentity = controlStore.GetOrCreateCertificate();
        using var reopenedIdentity = new ControllerIdentityStore(Path.Combine(tempRoot, "tls-control")).GetOrCreateCertificate();
        using var signingKey = reopenedIdentity.GetRSAPrivateKey();
        using var publicKey = originalIdentity.GetRSAPublicKey();
        var proof = "fixture"u8.ToArray();
        if (originalIdentity.Thumbprint != reopenedIdentity.Thumbprint || signingKey is null || publicKey is null ||
            !publicKey.VerifyData(proof, signingKey.SignData(proof, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new InvalidOperationException("Persisted Controller PFX lost its identity or signing key.");
        received = 0;
        var wrongPin = new string('0', 64);
        try { await client.PairAsync("127.0.0.1", port, "00000000", wrongPin); }
        catch (HttpRequestException) { }
        try { await client.RequestConnectionAsync("127.0.0.1", port, wrongPin); }
        catch (HttpRequestException) { }
        if (received != 0 || store.LoadAgents().Count != 0)
            throw new InvalidOperationException($"Initial TLS pin mismatch delivered {received} requests and persisted {store.LoadAgents().Count} agents.");
        var missingRoot = Path.Combine(tempRoot, "invalid-pin-controller");
        var invalidClient = new RemoteAgentClient(new ControllerIdentityStore(missingRoot));
        try { await invalidClient.PairAsync("127.0.0.1", port, "00000000", "invalid");
            throw new InvalidOperationException("Malformed pairing pin accepted."); }
        catch (ArgumentException) { }
        try { await invalidClient.RequestConnectionAsync("127.0.0.1", port, "");
            throw new InvalidOperationException("Missing request pin accepted."); }
        catch (ArgumentException) { }
        if (Directory.Exists(missingRoot) || received != 0)
            throw new InvalidOperationException("Invalid pin created an identity or transmitted a request.");
        Console.WriteLine("PASS initial TLS pin before both POSTs, malformed pins and Controller PFX reopening.");
    }
}
