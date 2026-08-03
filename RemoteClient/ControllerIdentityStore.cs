using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace UpdateCenter.RemoteClient;

public sealed class ControllerIdentityStore
{
    private static readonly byte[] CertificateEntropy = Encoding.UTF8.GetBytes("UpdateCenter.Controller.Certificate.v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly string _rootDirectory;
    private readonly string _identityPath;
    private readonly string _agentsPath;

    public ControllerIdentityStore(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UpdateCenter",
            "Controller");
        _identityPath = Path.Combine(_rootDirectory, "identity.bin");
        _agentsPath = Path.Combine(_rootDirectory, "agents.json");
    }

    public X509Certificate2 GetOrCreateCertificate()
    {
        Directory.CreateDirectory(_rootDirectory);
        if (File.Exists(_identityPath))
        {
            var protectedPfx = File.ReadAllBytes(_identityPath);
            var pfx = ProtectedData.Unprotect(protectedPfx, CertificateEntropy, DataProtectionScope.CurrentUser);
            return new X509Certificate2(
                pfx,
                (string?)null,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
        }

        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(
            $"CN=Update Center Controller {Environment.MachineName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.2")],
            true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(5));
        var protectedBytes = ProtectedData.Protect(
            certificate.Export(X509ContentType.Pfx),
            CertificateEntropy,
            DataProtectionScope.CurrentUser);
        var temporary = _identityPath + ".tmp";
        File.WriteAllBytes(temporary, protectedBytes);
        File.Move(temporary, _identityPath, overwrite: true);
        return new X509Certificate2(
            certificate.Export(X509ContentType.Pfx),
            (string?)null,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
    }

    public IReadOnlyList<PairedAgentRecord> LoadAgents()
    {
        try
        {
            if (!File.Exists(_agentsPath)) return [];
            var records = JsonSerializer.Deserialize<List<PairedAgentRecord>>(
                              File.ReadAllBytes(_agentsPath), JsonOptions) ?? [];
            return records
                .GroupBy(x => x.Address, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.OrderByDescending(record => record.PairedUtc).First())
                .GroupBy(x => x.AgentId)
                .Select(x => x.OrderByDescending(record => record.PairedUtc).First())
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public PairedAgentRecord? FindByAddress(string address) => LoadAgents()
        .Where(x => x.Address.Equals(address, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(x => x.PairedUtc)
        .FirstOrDefault();

    public void SaveAgent(PairedAgentRecord record)
    {
        Directory.CreateDirectory(_rootDirectory);
        var agents = LoadAgents().Where(x => x.AgentId != record.AgentId &&
            !x.Address.Equals(record.Address, StringComparison.OrdinalIgnoreCase)).ToList();
        agents.Add(record);
        var temporary = _agentsPath + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(
            agents.OrderBy(x => x.DisplayName).ToList(),
            JsonOptions));
        File.Move(temporary, _agentsPath, overwrite: true);
    }

    public bool RemoveAgent(Guid agentId)
    {
        var agents = LoadAgents().ToList();
        var removed = agents.RemoveAll(x => x.AgentId == agentId) > 0;
        if (!removed) return false;
        Directory.CreateDirectory(_rootDirectory);
        var temporary = _agentsPath + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(
            agents.OrderBy(x => x.DisplayName).ToList(), JsonOptions));
        File.Move(temporary, _agentsPath, overwrite: true);
        return true;
    }

    public bool RemoveAgent(Guid agentId, string address)
    {
        var agents = LoadAgents().ToList();
        var removed = agents.RemoveAll(x => x.AgentId == agentId ||
            x.Address.Equals(address, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed) return false;
        Directory.CreateDirectory(_rootDirectory);
        var temporary = _agentsPath + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(
            agents.OrderBy(x => x.DisplayName).ToList(), JsonOptions));
        File.Move(temporary, _agentsPath, overwrite: true);
        return true;
    }
}

public sealed class PairedAgentRecord
{
    public Guid AgentId { get; init; }
    public string DisplayName { get; init; } = "";
    public string Address { get; init; } = "";
    public int ApiPort { get; init; }
    public string CertificateSha256 { get; init; } = "";
    public DateTime PairedUtc { get; init; }
}
