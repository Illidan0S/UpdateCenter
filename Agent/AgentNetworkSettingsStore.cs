using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting.WindowsServices;
using UpdateCenter.Contracts;

namespace UpdateCenter.Agent;

public sealed class AgentNetworkSettingsStore
{
    private static readonly byte[] CertificateEntropy = Encoding.UTF8.GetBytes("UpdateCenter.Agent.Certificate.v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly object _sync = new();
    private NetworkSettingsData _settings;
    private IReadOnlyList<NetworkScopeData> _cachedActiveScopes = [];
    private DateTime _activeScopesCachedUtc;

    public AgentNetworkSettingsStore()
    {
        Directory.CreateDirectory(AgentDataPaths.RootDirectory);
        _settings = Load() ?? CreateDefault();
        if (!_settings.ConnectionRequestsConfigured)
        {
            _settings.ConnectionRequestsAllowed = _settings.Enabled;
            _settings.ConnectionRequestsConfigured = true;
        }
        SaveLocked();
    }

    public AgentNetworkConfiguration GetConfiguration(bool restartRequired = false, int pendingConnectionRequestCount = 0)
    {
        lock (_sync)
        {
            var scopes = _settings.Scopes ?? [];
            var activeScopes = GetActiveScopesLocked();
            var scopeActive = scopes.Any(saved => activeScopes.Any(current => ScopeMatches(saved, current)));
            return new AgentNetworkConfiguration
            {
                Enabled = _settings.Enabled,
                AgentId = _settings.AgentId,
                DisplayName = _settings.DisplayName,
                DiscoveryPort = _settings.DiscoveryPort,
                ApiPort = _settings.ApiPort,
                HasController = !string.IsNullOrWhiteSpace(_settings.ControllerCertificateSha256),
                ControllerName = _settings.ControllerName,
                CertificateSha256 = GetCertificateSha256Locked(),
                RestartRequired = restartRequired,
                NetworkScopeName = scopes.Count == 0
                    ? "Nessuna rete configurata"
                    : string.Join(", ", scopes.Select(x => x.InterfaceName).Distinct(StringComparer.CurrentCultureIgnoreCase)),
                NetworkScopeActive = _settings.Enabled && scopeActive,
                AllowedSubnets = scopes.Select(x => $"{x.NetworkAddress}/{x.PrefixLength}").Distinct().ToList(),
                ConnectionRequestsEnabled = _settings.Enabled &&
                                            string.IsNullOrWhiteSpace(_settings.ControllerCertificateSha256) &&
                                            (_settings.ConnectionRequestsAllowed ||
                                             _settings.ConnectionRequestsExpiresUtc > DateTime.UtcNow),
                ConnectionRequestsExpiresUtc = _settings.ConnectionRequestsExpiresUtc,
                PendingConnectionRequestCount = pendingConnectionRequestCount
            };
        }
    }

    public AgentNetworkConfiguration Enable()
    {
        lock (_sync)
        {
            var scopes = CollectActiveScopes();
            if (scopes.Count == 0)
                throw new InvalidOperationException("Nessuna rete locale attiva con gateway IPv4 è stata rilevata.");
            EnsureCertificateLocked();
            _settings.Scopes = scopes.ToList();
            _settings.Enabled = true;
            _settings.ConnectionRequestsAllowed = true;
            _settings.ConnectionRequestsConfigured = true;
            _settings.ConnectionRequestsExpiresUtc = default;
            _cachedActiveScopes = scopes;
            _activeScopesCachedUtc = DateTime.UtcNow;
            SaveLocked();
            return GetConfiguration(restartRequired: true);
        }
    }

    public AgentNetworkConfiguration Disable()
    {
        lock (_sync)
        {
            _settings.Enabled = false;
            _settings.Scopes = [];
            _settings.ConnectionRequestsAllowed = false;
            _settings.ConnectionRequestsConfigured = true;
            _settings.ConnectionRequestsExpiresUtc = default;
            SaveLocked();
            return GetConfiguration(restartRequired: true);
        }
    }

    public bool IsRemoteAddressAllowed(IPAddress? address)
    {
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        lock (_sync)
        {
            if (!_settings.Enabled || _settings.Scopes is not { Count: > 0 }) return false;
            var active = GetActiveScopesLocked();
            return _settings.Scopes.Any(saved =>
                active.Any(current => ScopeMatches(saved, current)) && Contains(saved, address));
        }
    }

    public void PairController(string name, X509Certificate2 certificate)
    {
        var certificateSha256 = CertificateFingerprint(certificate);
        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(_settings.ControllerCertificateSha256) &&
                !_settings.ControllerCertificateSha256.Equals(certificateSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Questo Agent è già associato a un altro Controller.");

            _settings.ControllerName = Limit(name, 128);
            _settings.ControllerCertificateSha256 = certificateSha256;
            _settings.ControllerCertificateBase64 = Convert.ToBase64String(certificate.Export(X509ContentType.Cert));
            _settings.ConnectionRequestsExpiresUtc = default;
            SaveLocked();
        }
    }

    public AgentNetworkConfiguration EnableConnectionRequests()
    {
        lock (_sync)
        {
            if (!_settings.Enabled)
                throw new InvalidOperationException("Abilita prima la gestione di rete.");
            if (!string.IsNullOrWhiteSpace(_settings.ControllerCertificateSha256))
                throw new InvalidOperationException("Questo PC è già collegato a un Controller.");
            _settings.ConnectionRequestsAllowed = true;
            _settings.ConnectionRequestsConfigured = true;
            _settings.ConnectionRequestsExpiresUtc = default;
            SaveLocked();
            return GetConfiguration();
        }
    }

    public AgentNetworkConfiguration DisableConnectionRequests()
    {
        lock (_sync)
        {
            _settings.ConnectionRequestsAllowed = false;
            _settings.ConnectionRequestsConfigured = true;
            _settings.ConnectionRequestsExpiresUtc = default;
            SaveLocked();
            return GetConfiguration();
        }
    }

    public AgentNetworkConfiguration RevokeController()
    {
        lock (_sync)
        {
            _settings.ControllerName = "";
            _settings.ControllerCertificateSha256 = "";
            _settings.ControllerCertificateBase64 = "";
            SaveLocked();
            return GetConfiguration();
        }
    }

    public X509Certificate2? GetControllerCertificate()
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(_settings.ControllerCertificateBase64)) return null;
            try
            {
                return new X509Certificate2(Convert.FromBase64String(_settings.ControllerCertificateBase64));
            }
            catch (CryptographicException)
            {
                return null;
            }
        }
    }

    public string GetControllerCertificateSha256()
    {
        lock (_sync) return _settings.ControllerCertificateSha256;
    }

    public X509Certificate2 GetServerCertificate()
    {
        lock (_sync)
        {
            EnsureCertificateLocked();
            var protectedBytes = Convert.FromBase64String(_settings.ProtectedCertificatePfx);
            var pfxBytes = ProtectedData.Unprotect(
                protectedBytes,
                CertificateEntropy,
                DataProtectionScope.LocalMachine);
            var keyStorage = WindowsServiceHelpers.IsWindowsService()
                ? X509KeyStorageFlags.MachineKeySet
                : X509KeyStorageFlags.UserKeySet;
            return new X509Certificate2(pfxBytes, (string?)null, keyStorage);
        }
    }

    public static string CertificateFingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData));

    private void EnsureCertificateLocked()
    {
        if (!string.IsNullOrWhiteSpace(_settings.ProtectedCertificatePfx)) return;
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(
            $"CN=Update Center Agent {_settings.AgentId:D}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(Environment.MachineName);
        san.AddDnsName("localhost");
        san.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(5));
        var pfx = certificate.Export(X509ContentType.Pfx);
        var protectedPfx = ProtectedData.Protect(pfx, CertificateEntropy, DataProtectionScope.LocalMachine);
        _settings.ProtectedCertificatePfx = Convert.ToBase64String(protectedPfx);
        _settings.CertificateSha256 = CertificateFingerprint(certificate);
    }

    private string GetCertificateSha256Locked()
    {
        if (string.IsNullOrWhiteSpace(_settings.ProtectedCertificatePfx)) return "";
        if (!string.IsNullOrWhiteSpace(_settings.CertificateSha256)) return _settings.CertificateSha256;
        using var certificate = GetServerCertificate();
        _settings.CertificateSha256 = CertificateFingerprint(certificate);
        SaveLocked();
        return _settings.CertificateSha256;
    }

    private NetworkSettingsData? Load()
    {
        try
        {
            if (!File.Exists(AgentDataPaths.NetworkSettingsFile)) return null;
            return JsonSerializer.Deserialize<NetworkSettingsData>(
                File.ReadAllBytes(AgentDataPaths.NetworkSettingsFile),
                JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static NetworkSettingsData CreateDefault() => new()
    {
        AgentId = Guid.NewGuid(),
        DisplayName = Environment.MachineName,
        DiscoveryPort = DiscoveryProtocol.DefaultPort,
        ApiPort = 47382
    };

    private IReadOnlyList<NetworkScopeData> GetActiveScopesLocked()
    {
        if (DateTime.UtcNow - _activeScopesCachedUtc < TimeSpan.FromSeconds(5)) return _cachedActiveScopes;
        _cachedActiveScopes = CollectActiveScopes();
        _activeScopesCachedUtc = DateTime.UtcNow;
        return _cachedActiveScopes;
    }

    private static IReadOnlyList<NetworkScopeData> CollectActiveScopes()
    {
        var scopes = new List<NetworkScopeData>();
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up ||
                network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            IPInterfaceProperties properties;
            try { properties = network.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }
            var gateway = properties.GatewayAddresses.Select(x => x.Address).FirstOrDefault(x =>
                x.AddressFamily == AddressFamily.InterNetwork && !x.Equals(IPAddress.Any));
            if (gateway is null) continue;
            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null ||
                    unicast.Address.GetAddressBytes()[0] == 169)
                    continue;
                var prefix = PrefixLength(unicast.IPv4Mask);
                if (prefix is <= 0 or > 32) continue;
                scopes.Add(new NetworkScopeData
                {
                    InterfaceId = network.Id,
                    InterfaceName = network.Name,
                    GatewayAddress = gateway.ToString(),
                    NetworkAddress = NetworkAddress(unicast.Address, unicast.IPv4Mask).ToString(),
                    PrefixLength = prefix
                });
            }
        }
        return scopes.GroupBy(x => $"{x.InterfaceId}|{x.GatewayAddress}|{x.NetworkAddress}|{x.PrefixLength}",
                StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First()).ToList();
    }

    private static bool ScopeMatches(NetworkScopeData left, NetworkScopeData right) =>
        left.InterfaceId.Equals(right.InterfaceId, StringComparison.OrdinalIgnoreCase) &&
        left.GatewayAddress.Equals(right.GatewayAddress, StringComparison.OrdinalIgnoreCase) &&
        left.NetworkAddress.Equals(right.NetworkAddress, StringComparison.OrdinalIgnoreCase) &&
        left.PrefixLength == right.PrefixLength;

    private static bool Contains(NetworkScopeData scope, IPAddress address)
    {
        if (!IPAddress.TryParse(scope.NetworkAddress, out var networkAddress)) return false;
        var network = ToUInt32(networkAddress);
        var candidate = ToUInt32(address);
        var mask = scope.PrefixLength == 0 ? 0u : uint.MaxValue << (32 - scope.PrefixLength);
        return (candidate & mask) == (network & mask);
    }

    private static IPAddress NetworkAddress(IPAddress address, IPAddress mask)
    {
        var addressBytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        return new IPAddress(addressBytes.Zip(maskBytes, (value, maskValue) => (byte)(value & maskValue)).ToArray());
    }

    private static int PrefixLength(IPAddress mask) => mask.GetAddressBytes().Sum(value =>
        System.Numerics.BitOperations.PopCount((uint)value));

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private void SaveLocked()
    {
        Directory.CreateDirectory(AgentDataPaths.RootDirectory);
        var temporary = AgentDataPaths.NetworkSettingsFile + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(_settings, JsonOptions));
        File.Move(temporary, AgentDataPaths.NetworkSettingsFile, overwrite: true);
    }

    private static string Limit(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }

    private sealed class NetworkSettingsData
    {
        public bool Enabled { get; set; }
        public Guid AgentId { get; set; }
        public string DisplayName { get; set; } = "";
        public int DiscoveryPort { get; set; }
        public int ApiPort { get; set; }
        public string ProtectedCertificatePfx { get; set; } = "";
        public string CertificateSha256 { get; set; } = "";
        public string ControllerName { get; set; } = "";
        public string ControllerCertificateSha256 { get; set; } = "";
        public string ControllerCertificateBase64 { get; set; } = "";
        public bool ConnectionRequestsAllowed { get; set; }
        public bool ConnectionRequestsConfigured { get; set; }
        public DateTime ConnectionRequestsExpiresUtc { get; set; }
        public List<NetworkScopeData> Scopes { get; set; } = [];
    }

    private sealed class NetworkScopeData
    {
        public string InterfaceId { get; set; } = "";
        public string InterfaceName { get; set; } = "";
        public string GatewayAddress { get; set; } = "";
        public string NetworkAddress { get; set; } = "";
        public int PrefixLength { get; set; }
    }
}
