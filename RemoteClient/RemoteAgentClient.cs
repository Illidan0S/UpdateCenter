using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using UpdateCenter.Contracts;

namespace UpdateCenter.RemoteClient;

public sealed class RemoteAgentClient(ControllerIdentityStore identityStore)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<PairedAgentRecord> PairAsync(
        string address,
        int port,
        string code,
        string displayName = "",
        CancellationToken cancellationToken = default)
    {
        using var controllerCertificate = identityStore.GetOrCreateCertificate();
        string observedFingerprint = "";
        using var handler = CreateHandler(certificate =>
        {
            observedFingerprint = Fingerprint(certificate);
            return true;
        });
        using var http = CreateHttpClient(address, port, handler);
        using var response = await http.PostAsJsonAsync("/api/v1/pair", new PairingRequest
        {
            Code = code,
            ControllerId = ControllerId(controllerCertificate),
            ControllerName = Environment.MachineName,
            ControllerCertificateBase64 = Convert.ToBase64String(
                controllerCertificate.Export(X509ContentType.Cert))
        }, JsonOptions, cancellationToken).ConfigureAwait(false);
        var pairing = await response.Content.ReadFromJsonAsync<PairingResponse>(JsonOptions, cancellationToken)
                      ?? throw new InvalidDataException("Risposta pairing non valida.");
        if (!response.IsSuccessStatusCode || !pairing.Success)
            throw new InvalidOperationException($"{pairing.ErrorCode}: {pairing.Message}");
        if (string.IsNullOrWhiteSpace(observedFingerprint) ||
            !FixedEquals(observedFingerprint, pairing.AgentCertificateSha256))
            throw new InvalidOperationException("Il certificato dell'Agent è cambiato durante il pairing.");

        var record = new PairedAgentRecord
        {
            AgentId = pairing.AgentId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? address : displayName,
            Address = address,
            ApiPort = port,
            CertificateSha256 = observedFingerprint,
            PairedUtc = DateTime.UtcNow
        };
        identityStore.SaveAgent(record);
        return record;
    }

    public async Task<PendingRemoteConnectionRequest> RequestConnectionAsync(
        string address,
        int port,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        using var controllerCertificate = identityStore.GetOrCreateCertificate();
        string observedFingerprint = "";
        using var handler = CreateHandler(certificate =>
        {
            observedFingerprint = Fingerprint(certificate);
            return true;
        });
        using var http = CreateHttpClient(address, port, handler);
        using var response = await http.PostAsJsonAsync("/api/v1/connection-requests", new ConnectionRequestCreate
        {
            ControllerId = ControllerId(controllerCertificate),
            ControllerName = Environment.MachineName,
            ControllerCertificateBase64 = Convert.ToBase64String(
                controllerCertificate.Export(X509ContentType.Cert))
        }, JsonOptions, cancellationToken).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<ConnectionRequestResponse>(JsonOptions, cancellationToken)
                     ?? throw new InvalidDataException("Risposta alla richiesta di collegamento non valida.");
        if (!response.IsSuccessStatusCode || !result.Success)
            throw new RemoteAgentException(result.ErrorCode, result.Message, (int)response.StatusCode);
        if (result.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(result.PollToken) ||
            string.IsNullOrWhiteSpace(observedFingerprint) ||
            !FixedEquals(observedFingerprint, result.AgentCertificateSha256))
            throw new InvalidDataException("Identità dell'Agent non verificabile durante la richiesta.");
        return new PendingRemoteConnectionRequest(
            result.RequestId,
            address,
            port,
            displayName,
            observedFingerprint,
            result.AgentId,
            result.PollToken,
            result.ExpiresUtc);
    }

    public async Task<ConnectionRequestResponse> GetConnectionRequestStatusAsync(
        PendingRemoteConnectionRequest pending,
        CancellationToken cancellationToken = default)
    {
        using var handler = CreateHandler(
            certificate => FixedEquals(Fingerprint(certificate), pending.AgentCertificateSha256));
        using var http = CreateHttpClient(pending.Address, pending.ApiPort, handler);
        using var response = await http.PostAsJsonAsync(
            $"/api/v1/connection-requests/{pending.RequestId:D}/status",
            new ConnectionRequestStatusQuery { PollToken = pending.PollToken },
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<ConnectionRequestResponse>(JsonOptions, cancellationToken)
                     ?? throw new InvalidDataException("Stato della richiesta di collegamento non valido.");
        if (!response.IsSuccessStatusCode || !result.Success)
            throw new RemoteAgentException(result.ErrorCode, result.Message, (int)response.StatusCode);
        if (!FixedEquals(pending.AgentCertificateSha256, result.AgentCertificateSha256) ||
            pending.AgentId != result.AgentId)
            throw new InvalidDataException("L'identità dell'Agent è cambiata durante l'approvazione.");
        if (result.Status == ConnectionRequestStates.Accepted)
        {
            identityStore.SaveAgent(new PairedAgentRecord
            {
                AgentId = pending.AgentId,
                DisplayName = string.IsNullOrWhiteSpace(pending.DisplayName) ? pending.Address : pending.DisplayName,
                Address = pending.Address,
                ApiPort = pending.ApiPort,
                CertificateSha256 = pending.AgentCertificateSha256,
                PairedUtc = DateTime.UtcNow
            });
        }
        return result;
    }

    public Task<AgentResponse> GetStatusAsync(string address, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, address, "/api/v1/status", null, cancellationToken);

    public Task<AgentResponse> StartScanAsync(
        string address,
        ScanRequest? request = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, address, "/api/v1/scans", request ?? new ScanRequest(), cancellationToken);

    public Task<AgentResponse> StartUpdateAsync(
        string address,
        RemoteUpdateRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, address, "/api/v1/updates", request, cancellationToken);

    public Task<AgentResponse> GetOperationAsync(
        string address,
        Guid operationId,
        CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, address, $"/api/v1/operations/{operationId:D}", null, cancellationToken);

    public Task<AgentResponse> CancelOperationAsync(
        string address,
        Guid operationId,
        CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, address, $"/api/v1/operations/{operationId:D}", null, cancellationToken);

    private async Task<AgentResponse> SendAsync(
        HttpMethod method,
        string address,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        var record = identityStore.FindByAddress(address)
                     ?? throw new InvalidOperationException("Agent non associato a questo Controller.");
        using var controllerCertificate = identityStore.GetOrCreateCertificate();
        using var handler = CreateHandler(
            certificate => FixedEquals(Fingerprint(certificate), record.CertificateSha256));
        using var http = CreateHttpClient(record.Address, record.ApiPort, handler);
        using var request = new HttpRequestMessage(method, path);
        var bodyBytes = body is null ? [] : JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);
        if (bodyBytes.Length > 0)
        {
            request.Content = new ByteArrayContent(bodyBytes);
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("D");
        var canonical = SignedRequestProtocol.BuildCanonical(method.Method, path, timestamp, nonce, bodyBytes);
        using var privateKey = controllerCertificate.GetRSAPrivateKey()
                               ?? throw new InvalidOperationException("Chiave privata del Controller non disponibile.");
        var signature = privateKey.SignData(
            System.Text.Encoding.UTF8.GetBytes(canonical),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.Headers.Add(SignedRequestProtocol.ControllerHeader, Fingerprint(controllerCertificate));
        request.Headers.Add(SignedRequestProtocol.TimestampHeader, timestamp);
        request.Headers.Add(SignedRequestProtocol.NonceHeader, nonce);
        request.Headers.Add(SignedRequestProtocol.SignatureHeader, Convert.ToBase64String(signature));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<AgentResponse>(JsonOptions, cancellationToken)
                     ?? throw new InvalidDataException("Risposta Agent non valida.");
        if (!response.IsSuccessStatusCode || !result.Success)
            throw new RemoteAgentException(result.ErrorCode, result.Message, (int)response.StatusCode);
        return result;
    }

    private static HttpClientHandler CreateHandler(Func<X509Certificate2, bool> validateServer)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate is not null && validateServer(new X509Certificate2(certificate))
        };
        return handler;
    }

    private static HttpClient CreateHttpClient(string address, int port, HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri($"https://{FormatHost(address)}:{port}"),
        Timeout = TimeSpan.FromSeconds(45)
    };

    private static string FormatHost(string address) =>
        address.Contains(':', StringComparison.Ordinal) && !address.StartsWith("[", StringComparison.Ordinal)
            ? $"[{address}]"
            : address;

    private static string Fingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData));

    private static Guid ControllerId(X509Certificate2 certificate) =>
        new(SHA256.HashData(certificate.RawData).AsSpan(0, 16));

    private static bool FixedEquals(string left, string right)
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

public sealed record PendingRemoteConnectionRequest(
    Guid RequestId,
    string Address,
    int ApiPort,
    string DisplayName,
    string AgentCertificateSha256,
    Guid AgentId,
    string PollToken,
    DateTime ExpiresUtc);

public sealed class RemoteAgentException(string errorCode, string message, int statusCode)
    : InvalidOperationException($"{errorCode}: {message}")
{
    public string ErrorCode { get; } = errorCode;
    public int StatusCode { get; } = statusCode;
}
