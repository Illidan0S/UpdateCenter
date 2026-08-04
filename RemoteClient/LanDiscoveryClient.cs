using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using UpdateCenter.Contracts;

namespace UpdateCenter.RemoteClient;

public sealed class LanDiscoveryClient
{
    private const int MaximumConcurrentProbes = 64;
    private const int ProbePort = 47382;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<DiscoveredAgent>> DiscoverAsync(
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.EnableBroadcast = true;
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        var request = new DiscoveryRequest();
        var payload = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
        var localAddresses = GetLocalIPv4Addresses();
        foreach (var endpoint in GetDiscoveryEndpoints())
        {
            try { await udp.SendAsync(payload, endpoint, cancellationToken).ConfigureAwait(false); }
            catch (SocketException) { }
        }

        var found = new ConcurrentDictionary<Guid, DiscoveredAgent>();
        var receiveTask = ReceiveBroadcastResponsesAsync(
            udp, request, duration, localAddresses, found, cancellationToken);
        var probeTask = ProbeLocalSubnetsAsync(localAddresses, found, cancellationToken);
        await Task.WhenAll(receiveTask, probeTask).ConfigureAwait(false);
        return found.Values.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static async Task ReceiveBroadcastResponsesAsync(
        UdpClient udp,
        DiscoveryRequest request,
        TimeSpan duration,
        IReadOnlySet<IPAddress> localAddresses,
        ConcurrentDictionary<Guid, DiscoveredAgent> found,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(duration);
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var received = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                if (IPAddress.IsLoopback(received.RemoteEndPoint.Address) ||
                    localAddresses.Contains(received.RemoteEndPoint.Address))
                    continue;
                if (received.Buffer.Length is <= 0 or > DiscoveryProtocol.MaximumDatagramBytes) continue;
                var response = JsonSerializer.Deserialize<DiscoveredAgent>(received.Buffer, JsonOptions);
                if (response is null || response.Magic != DiscoveryProtocol.Magic ||
                    response.RequestId != request.RequestId || response.AgentId == Guid.Empty ||
                    response.ProtocolMajor != AgentProtocol.MajorVersion)
                    continue;
                found[response.AgentId] = WithAddress(response, received.RemoteEndPoint.Address.ToString());
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                break;
            }
            catch (JsonException) { }
            catch (SocketException) { }
        }
    }

    private static async Task ProbeLocalSubnetsAsync(
        IReadOnlySet<IPAddress> localAddresses,
        ConcurrentDictionary<Guid, DiscoveredAgent> found,
        CancellationToken cancellationToken)
    {
        var addresses = GetProbeAddresses(localAddresses);
        using var concurrency = new SemaphoreSlim(MaximumConcurrentProbes, MaximumConcurrentProbes);
        var tasks = addresses.Select(async address =>
        {
            await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var agent = await ProbeAsync(address, ProbePort, cancellationToken).ConfigureAwait(false);
                if (agent is not null) found[agent.AgentId] = agent;
            }
            finally
            {
                concurrency.Release();
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public Task<DiscoveredAgent?> ProbeAddressAsync(
        string address,
        int port,
        CancellationToken cancellationToken = default) =>
        IPAddress.TryParse(address, out var parsed) && port is > 0 and <= 65535
            ? ProbeAsync(parsed, port, cancellationToken)
            : Task.FromResult<DiscoveredAgent?>(null);

    private static async Task<DiscoveredAgent?> ProbeAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken)
    {
        string observedCertificate = "";
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null) return false;
                using var serverCertificate = new X509Certificate2(certificate);
                observedCertificate = Convert.ToHexString(SHA256.HashData(serverCertificate.RawData));
                return true;
            }
        };
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://{address}:{port}"),
            Timeout = TimeSpan.FromMilliseconds(700)
        };
        try
        {
            using var response = await http.GetAsync("/api/v1/discovery", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var agent = await JsonSerializer.DeserializeAsync<DiscoveredAgent>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (agent is null || agent.AgentId == Guid.Empty || agent.ProtocolMajor != AgentProtocol.MajorVersion ||
                string.IsNullOrWhiteSpace(observedCertificate) ||
                !observedCertificate.Equals(agent.CertificateSha256, StringComparison.OrdinalIgnoreCase))
                return null;
            return WithAddress(agent, address.ToString());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return null;
        }
    }

    private static IReadOnlyList<IPAddress> GetProbeAddresses(IReadOnlySet<IPAddress> localAddresses)
    {
        var addresses = new HashSet<IPAddress>();
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up ||
                network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            IPInterfaceProperties properties;
            try { properties = network.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }
            if (!properties.GatewayAddresses.Any(x => x.Address.AddressFamily == AddressFamily.InterNetwork)) continue;
            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null) continue;
                var prefix = Math.Max(PrefixLength(unicast.IPv4Mask), 24);
                if (prefix > 30) continue;
                var local = ToUInt32(unicast.Address);
                var mask = uint.MaxValue << (32 - prefix);
                var networkAddress = local & mask;
                var hostCount = (1u << (32 - prefix)) - 1;
                for (uint host = 1; host < hostCount; host++)
                {
                    var candidate = networkAddress + host;
                    var candidateAddress = FromUInt32(candidate);
                    if (candidate != local && !localAddresses.Contains(candidateAddress)) addresses.Add(candidateAddress);
                }
            }
        }
        return addresses.ToList();
    }

    private static IReadOnlyList<IPEndPoint> GetDiscoveryEndpoints()
    {
        var addresses = new HashSet<IPAddress> { IPAddress.Broadcast };
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up ||
                network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            foreach (var unicast in network.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null) continue;
                var address = unicast.Address.GetAddressBytes();
                var mask = unicast.IPv4Mask.GetAddressBytes();
                var broadcast = new byte[4];
                for (var index = 0; index < 4; index++) broadcast[index] = (byte)(address[index] | ~mask[index]);
                addresses.Add(new IPAddress(broadcast));
            }
        }
        return addresses.Select(x => new IPEndPoint(x, DiscoveryProtocol.DefaultPort)).ToList();
    }

    public static IReadOnlySet<IPAddress> GetLocalIPv4Addresses()
    {
        var addresses = new HashSet<IPAddress> { IPAddress.Loopback };
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var unicast in network.GetIPProperties().UnicastAddresses)
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    addresses.Add(unicast.Address);
        }
        return addresses;
    }

    private static DiscoveredAgent WithAddress(DiscoveredAgent source, string address) => new()
    {
        RequestId = source.RequestId,
        AgentId = source.AgentId,
        DisplayName = source.DisplayName,
        MachineName = source.MachineName,
        Address = address,
        ApiPort = source.ApiPort,
        ProtocolMajor = source.ProtocolMajor,
        ProtocolMinor = source.ProtocolMinor,
        AgentVersion = source.AgentVersion,
        CertificateSha256 = source.CertificateSha256,
        HasController = source.HasController,
        ConnectionRequestsEnabled = source.ConnectionRequestsEnabled,
        ConnectionRequestsExpiresUtc = source.ConnectionRequestsExpiresUtc
    };

    private static int PrefixLength(IPAddress mask) => mask.GetAddressBytes().Sum(value =>
        System.Numerics.BitOperations.PopCount((uint)value));

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress FromUInt32(uint address) => new(new[]
    {
        (byte)(address >> 24), (byte)(address >> 16), (byte)(address >> 8), (byte)address
    });
}
