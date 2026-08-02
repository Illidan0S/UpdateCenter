using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using UpdateCenter.Contracts;

namespace UpdateCenter.Agent;

public sealed class AgentDiscoveryWorker(
    ILogger<AgentDiscoveryWorker> logger,
    AgentNetworkSettingsStore settingsStore) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<IPAddress, DateTime> _lastResponses = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = settingsStore.GetConfiguration();
        if (!configuration.Enabled)
        {
            logger.LogInformation("Discovery LAN disabilitato.");
            return;
        }

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.ReceiveBufferSize = DiscoveryProtocol.MaximumDatagramBytes * 4;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, configuration.DiscoveryPort));
        logger.LogInformation("Discovery LAN attivo su UDP {Port}.", configuration.DiscoveryPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await udp.ReceiveAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException ex)
            {
                logger.LogWarning(ex, "Datagramma discovery non ricevibile.");
                continue;
            }

            if (received.Buffer.Length is <= 0 or > DiscoveryProtocol.MaximumDatagramBytes) continue;
            DiscoveryRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<DiscoveryRequest>(received.Buffer, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }
            if (request is null || request.Magic != DiscoveryProtocol.Magic ||
                request.ProtocolMajor != AgentProtocol.MajorVersion)
                continue;
            if (!settingsStore.IsRemoteAddressAllowed(received.RemoteEndPoint.Address)) continue;
            if (!CanRespond(received.RemoteEndPoint.Address)) continue;

            configuration = settingsStore.GetConfiguration();
            var response = new DiscoveredAgent
            {
                RequestId = request.RequestId,
                AgentId = configuration.AgentId,
                DisplayName = configuration.DisplayName,
                MachineName = Environment.MachineName,
                ApiPort = configuration.ApiPort,
                ProtocolMajor = AgentProtocol.MajorVersion,
                ProtocolMinor = AgentProtocol.MinorVersion,
                AgentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0",
                CertificateSha256 = configuration.CertificateSha256,
                HasController = configuration.HasController,
                ConnectionRequestsEnabled = configuration.ConnectionRequestsEnabled,
                ConnectionRequestsExpiresUtc = configuration.ConnectionRequestsExpiresUtc
            };
            var payload = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
            if (payload.Length > DiscoveryProtocol.MaximumDatagramBytes) continue;
            try
            {
                await udp.SendAsync(payload, received.RemoteEndPoint, stoppingToken).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                logger.LogDebug(ex, "Risposta discovery non inviata a {Address}.", received.RemoteEndPoint.Address);
            }
        }
    }

    private bool CanRespond(IPAddress address)
    {
        var now = DateTime.UtcNow;
        if (_lastResponses.TryGetValue(address, out var previous) && now - previous < TimeSpan.FromMilliseconds(750))
            return false;
        _lastResponses[address] = now;
        if (_lastResponses.Count > 512)
        {
            var expiry = now.AddMinutes(-5);
            foreach (var entry in _lastResponses.Where(x => x.Value < expiry))
                _lastResponses.TryRemove(entry.Key, out _);
        }
        return true;
    }
}
