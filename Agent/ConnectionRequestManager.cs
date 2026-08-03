using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting.WindowsServices;
using UpdateCenter.Contracts;

namespace UpdateCenter.Agent;

public sealed class ConnectionRequestManager(
    ILogger<ConnectionRequestManager> logger,
    AgentNetworkSettingsStore settingsStore)
{
    private const int MaximumPendingRequests = 50;
    private static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MinimumIntervalPerAddress = TimeSpan.FromSeconds(10);
    private readonly object _sync = new();
    private readonly Dictionary<Guid, RequestEntry> _requests = [];
    private readonly Dictionary<string, DateTime> _lastRequestByAddress = new(StringComparer.OrdinalIgnoreCase);

    public int PendingCount
    {
        get
        {
            lock (_sync)
            {
                CleanupLocked();
                return _requests.Values.Count(x => x.Status == ConnectionRequestStates.Pending);
            }
        }
    }

    public ConnectionRequestResponse Create(ConnectionRequestCreate request, string remoteAddress)
    {
        var configuration = settingsStore.GetConfiguration();
        if (!configuration.ConnectionRequestsEnabled)
            return Error("RequestsDisabled", "Il PC non sta accettando richieste di collegamento.");
        if (configuration.HasController)
            return Error("ControllerAlreadyPaired", "Questo PC è già collegato a un Controller.");
        if (request.ControllerId == Guid.Empty || string.IsNullOrWhiteSpace(request.ControllerName) ||
            request.ControllerName.Length > 128 || string.IsNullOrWhiteSpace(request.ControllerCertificateBase64) ||
            request.ControllerCertificateBase64.Length > 16 * 1024)
            return Error("InvalidRequest", "Identità del Controller mancante o non valida.");

        X509Certificate2 certificate;
        string fingerprint;
        try
        {
            certificate = new X509Certificate2(Convert.FromBase64String(request.ControllerCertificateBase64));
            using var publicKey = certificate.GetRSAPublicKey();
            if (publicKey is null || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
                throw new CryptographicException();
            fingerprint = AgentNetworkSettingsStore.CertificateFingerprint(certificate);
            var expectedControllerId = new Guid(SHA256.HashData(certificate.RawData).AsSpan(0, 16));
            if (request.ControllerId != expectedControllerId) throw new CryptographicException();
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return Error("InvalidControllerCertificate", "Certificato del Controller non valido.");
        }

        using (certificate)
        {
            RequestEntry entry;
            string pollToken;
            lock (_sync)
            {
                CleanupLocked();
                var now = DateTime.UtcNow;
                if (_lastRequestByAddress.TryGetValue(remoteAddress, out var lastRequest) &&
                    now - lastRequest < MinimumIntervalPerAddress)
                    return Error("RateLimited", "Attendi alcuni secondi prima di inviare un'altra richiesta.");
                if (_requests.Values.Count(x => x.Status == ConnectionRequestStates.Pending) >= MaximumPendingRequests)
                    return Error("TooManyRequests", "Il PC ha già troppe richieste in attesa.");
                if (_requests.Values.Any(x => x.Status == ConnectionRequestStates.Pending &&
                                              x.ControllerFingerprint.Equals(fingerprint, StringComparison.OrdinalIgnoreCase)))
                    return Error("RequestAlreadyPending", "Una richiesta di questo Controller è già in attesa.");

                pollToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                entry = new RequestEntry
                {
                    Id = Guid.NewGuid(),
                    ControllerId = request.ControllerId,
                    ControllerName = request.ControllerName.Trim(),
                    ControllerCertificateBase64 = request.ControllerCertificateBase64,
                    ControllerFingerprint = fingerprint,
                    RemoteAddress = remoteAddress,
                    RequestedUtc = now,
                    ExpiresUtc = now.Add(RequestLifetime),
                    PollTokenHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(pollToken))
                };
                _requests.Add(entry.Id, entry);
                _lastRequestByAddress[remoteAddress] = now;
            }

            TryLaunchNotification(entry.Id);
            return BuildResponse(entry, pollToken, "Richiesta inviata al dispositivo.");
        }
    }

    public ConnectionRequestResponse GetStatus(Guid requestId, string pollToken)
    {
        lock (_sync)
        {
            CleanupLocked();
            if (!_requests.TryGetValue(requestId, out var entry))
                return Error("RequestNotFound", "Richiesta di collegamento non trovata.");
            var candidateHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(pollToken ?? ""));
            if (!CryptographicOperations.FixedTimeEquals(candidateHash, entry.PollTokenHash))
                return Error("Unauthorized", "Token di verifica non valido.");
            return BuildResponse(entry, "", "Stato della richiesta aggiornato.");
        }
    }

    public IReadOnlyList<PendingConnectionRequest> GetPending()
    {
        lock (_sync)
        {
            CleanupLocked();
            return _requests.Values
                .Where(x => x.Status == ConnectionRequestStates.Pending)
                .OrderBy(x => x.RequestedUtc)
                .Select(ToContract)
                .ToList();
        }
    }

    public PendingConnectionRequest? GetPending(Guid requestId) =>
        GetPending().FirstOrDefault(x => x.RequestId == requestId);

    public bool Decide(Guid requestId, bool accept, out string message)
    {
        lock (_sync)
        {
            CleanupLocked();
            if (!_requests.TryGetValue(requestId, out var entry) ||
                entry.Status != ConnectionRequestStates.Pending)
            {
                message = "La richiesta non esiste più oppure è scaduta.";
                return false;
            }

            if (!accept)
            {
                entry.Status = ConnectionRequestStates.Rejected;
                entry.CompletedUtc = DateTime.UtcNow;
                message = "Richiesta rifiutata.";
                return true;
            }

            try
            {
                using var certificate = new X509Certificate2(Convert.FromBase64String(entry.ControllerCertificateBase64));
                settingsStore.PairController(entry.ControllerName, certificate);
                entry.Status = ConnectionRequestStates.Accepted;
                entry.CompletedUtc = DateTime.UtcNow;
                foreach (var other in _requests.Values.Where(x => x.Id != entry.Id &&
                                                                   x.Status == ConnectionRequestStates.Pending))
                {
                    other.Status = ConnectionRequestStates.Rejected;
                    other.CompletedUtc = DateTime.UtcNow;
                }
                message = $"Collegamento con {entry.ControllerName} autorizzato.";
                return true;
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException or InvalidOperationException)
            {
                message = ex.Message;
                return false;
            }
        }
    }

    public void ClearPending()
    {
        lock (_sync)
        {
            foreach (var entry in _requests.Values.Where(x => x.Status == ConnectionRequestStates.Pending))
            {
                entry.Status = ConnectionRequestStates.Rejected;
                entry.CompletedUtc = DateTime.UtcNow;
            }
        }
    }

    private void TryLaunchNotification(Guid requestId)
    {
        try
        {
            var executable = Path.Combine(AppContext.BaseDirectory, "UpdateCenter.exe");
            if (!File.Exists(executable))
            {
                logger.LogWarning("UpdateCenter.exe non disponibile per mostrare la richiesta {RequestId}.", requestId);
                return;
            }
            using var launcher = InteractiveSessionLauncher.Prepare(WindowsServiceHelpers.IsWindowsService());
            using var process = launcher.Start(
                executable,
                ["--connection-request", requestId.ToString("D")],
                AppContext.BaseDirectory);
            logger.LogInformation("Notifica di collegamento {RequestId} avviata nella sessione utente.", requestId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Impossibile mostrare la notifica di collegamento {RequestId}.", requestId);
        }
    }

    private void CleanupLocked()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in _requests.Values.Where(x => x.Status == ConnectionRequestStates.Pending &&
                                                           x.ExpiresUtc <= now))
        {
            entry.Status = ConnectionRequestStates.Expired;
            entry.CompletedUtc = now;
        }
        foreach (var id in _requests.Where(x => x.Value.CompletedUtc is DateTime completed &&
                                                now - completed > Retention)
                     .Select(x => x.Key).ToList())
            _requests.Remove(id);
        foreach (var address in _lastRequestByAddress.Where(x => now - x.Value > TimeSpan.FromMinutes(1))
                     .Select(x => x.Key).ToList())
            _lastRequestByAddress.Remove(address);
    }

    private ConnectionRequestResponse BuildResponse(RequestEntry entry, string pollToken, string message)
    {
        var configuration = settingsStore.GetConfiguration();
        return new ConnectionRequestResponse
        {
            Success = true,
            Message = message,
            RequestId = entry.Id,
            Status = entry.Status,
            PollToken = pollToken,
            ExpiresUtc = entry.ExpiresUtc,
            AgentId = configuration.AgentId,
            AgentCertificateSha256 = configuration.CertificateSha256
        };
    }

    private static PendingConnectionRequest ToContract(RequestEntry entry) => new()
    {
        RequestId = entry.Id,
        ControllerId = entry.ControllerId,
        ControllerName = entry.ControllerName,
        ControllerCertificateSha256 = entry.ControllerFingerprint,
        RemoteAddress = entry.RemoteAddress,
        RequestedUtc = entry.RequestedUtc,
        ExpiresUtc = entry.ExpiresUtc,
        Status = entry.Status
    };

    private static ConnectionRequestResponse Error(string code, string message) => new()
    {
        ErrorCode = code,
        Message = message,
        Status = ConnectionRequestStates.Rejected
    };

    private sealed class RequestEntry
    {
        public Guid Id { get; init; }
        public Guid ControllerId { get; init; }
        public string ControllerName { get; init; } = "";
        public string ControllerCertificateBase64 { get; init; } = "";
        public string ControllerFingerprint { get; init; } = "";
        public string RemoteAddress { get; init; } = "";
        public DateTime RequestedUtc { get; init; }
        public DateTime ExpiresUtc { get; init; }
        public byte[] PollTokenHash { get; init; } = [];
        public string Status { get; set; } = ConnectionRequestStates.Pending;
        public DateTime? CompletedUtc { get; set; }
    }
}
