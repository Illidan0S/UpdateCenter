using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Http;
using UpdateCenter.Contracts;

namespace UpdateCenter.Agent;

public sealed class SignedRequestVerifier(AgentNetworkSettingsStore settingsStore)
{
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<Guid, DateTime> _usedNonces = new();

    public async Task<bool> VerifyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var fingerprint = request.Headers[SignedRequestProtocol.ControllerHeader].ToString();
        var timestampText = request.Headers[SignedRequestProtocol.TimestampHeader].ToString();
        var nonceText = request.Headers[SignedRequestProtocol.NonceHeader].ToString();
        var signatureText = request.Headers[SignedRequestProtocol.SignatureHeader].ToString();
        if (fingerprint.Length != 64 || timestampText.Length > 20 || nonceText.Length > 40 ||
            signatureText.Length is <= 0 or > 1_024)
            return false;
        if (!long.TryParse(timestampText, out var unixSeconds) || !Guid.TryParse(nonceText, out var nonce))
            return false;
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if ((DateTimeOffset.UtcNow - timestamp).Duration() > AllowedClockSkew) return false;

        var expectedFingerprint = settingsStore.GetControllerCertificateSha256();
        if (!FixedHexEquals(fingerprint, expectedFingerprint)) return false;
        using var certificate = settingsStore.GetControllerCertificate();
        using var rsa = certificate?.GetRSAPublicKey();
        if (rsa is null) return false;

        request.EnableBuffering(bufferThreshold: 32 * 1024, bufferLimit: 64 * 1024);
        byte[] body;
        try
        {
            using var memory = new MemoryStream();
            await request.Body.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            body = memory.ToArray();
            request.Body.Position = 0;
        }
        catch (IOException)
        {
            return false;
        }

        byte[] signature;
        try { signature = Convert.FromBase64String(signatureText); }
        catch (FormatException) { return false; }
        var canonical = SignedRequestProtocol.BuildCanonical(
            request.Method,
            request.Path + request.QueryString,
            timestampText,
            nonceText,
            body);
        var valid = rsa.VerifyData(
            Encoding.UTF8.GetBytes(canonical),
            signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        if (!valid || !_usedNonces.TryAdd(nonce, DateTime.UtcNow)) return false;
        TrimNonces();
        return true;
    }

    private void TrimNonces()
    {
        if (_usedNonces.Count <= 2_048) return;
        var expiry = DateTime.UtcNow - AllowedClockSkew - TimeSpan.FromMinutes(1);
        foreach (var item in _usedNonces.Where(x => x.Value < expiry))
            _usedNonces.TryRemove(item.Key, out _);
    }

    private static bool FixedHexEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left),
                Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
