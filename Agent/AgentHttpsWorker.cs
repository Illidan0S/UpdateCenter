using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using UpdateCenter.Contracts;
using UpdateCenter.Core;

namespace UpdateCenter.Agent;

public sealed class AgentHttpsWorker(
    ILogger<AgentHttpsWorker> logger,
    AgentNetworkSettingsStore settingsStore,
    PairingCodeManager pairingCodes,
    ConnectionRequestManager connectionRequests,
    SignedRequestVerifier signedRequests) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = settingsStore.GetConfiguration();
        if (!configuration.Enabled)
        {
            logger.LogInformation("API HTTPS LAN disabilitata.");
            return;
        }

        using var serverCertificate = settingsStore.GetServerCertificate();
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(AgentHttpsWorker).Assembly.FullName,
            ContentRootPath = AppContext.BaseDirectory,
            Args = []
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 32 * 1024;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            options.ListenAnyIP(configuration.ApiPort, listen => listen.UseHttps(new HttpsConnectionAdapterOptions
            {
                ServerCertificate = serverCertificate,
                ClientCertificateMode = ClientCertificateMode.NoCertificate,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                               System.Security.Authentication.SslProtocols.Tls13
            }));
        });
        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            if (!settingsStore.IsRemoteAddressAllowed(context.Connection.RemoteIpAddress))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    AgentResponse.Error(Guid.Empty, "NetworkNotAllowed", "La richiesta non proviene dalla rete locale autorizzata."),
                    context.RequestAborted).ConfigureAwait(false);
                return;
            }
            if (context.Request.Path.StartsWithSegments("/api/v1") &&
                !context.Request.Path.Equals("/api/v1/pair") &&
                !context.Request.Path.Equals("/api/v1/discovery") &&
                !context.Request.Path.StartsWithSegments("/api/v1/connection-requests") &&
                !await signedRequests.VerifyAsync(context.Request, context.RequestAborted).ConfigureAwait(false))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    AgentResponse.Error(Guid.Empty, "Unauthorized", "Firma Controller non valida o richiesta già utilizzata."),
                    context.RequestAborted).ConfigureAwait(false);
                return;
            }
            await next(context).ConfigureAwait(false);
        });

        app.MapPost("/api/v1/pair", (Func<PairingRequest, IResult>)Pair);
        app.MapPost("/api/v1/connection-requests",
            (Func<HttpContext, ConnectionRequestCreate, IResult>)CreateConnectionRequest);
        app.MapPost("/api/v1/connection-requests/{requestId:guid}/status",
            (Func<Guid, ConnectionRequestStatusQuery, IResult>)ConnectionRequestStatus);
        app.MapGet("/api/v1/discovery", (Func<IResult>)Discovery);
        app.MapGet("/api/v1/status", (Func<HttpContext, Task<IResult>>)StatusAsync);
        app.MapPost("/api/v1/scans", (Func<HttpContext, ScanRequest?, Task<IResult>>)StartScanAsync);
        app.MapPost("/api/v1/updates", (Func<HttpContext, RemoteUpdateRequest?, Task<IResult>>)StartUpdateAsync);
        app.MapGet("/api/v1/operations/{operationId:guid}",
            (Func<HttpContext, Guid, Task<IResult>>)GetOperationAsync);
        app.MapDelete("/api/v1/operations/{operationId:guid}",
            (Func<HttpContext, Guid, Task<IResult>>)CancelOperationAsync);

        logger.LogInformation("API HTTPS LAN attiva su TCP {Port} in modalità sola lettura.", configuration.ApiPort);
        await app.RunAsync(stoppingToken).ConfigureAwait(false);
        return;

        IResult Discovery()
        {
            var current = settingsStore.GetConfiguration();
            return Results.Json(new DiscoveredAgent
            {
                AgentId = current.AgentId,
                DisplayName = current.DisplayName,
                MachineName = Environment.MachineName,
                ApiPort = current.ApiPort,
                ProtocolMajor = AgentProtocol.MajorVersion,
                ProtocolMinor = AgentProtocol.MinorVersion,
                AgentVersion = typeof(AgentHttpsWorker).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                CertificateSha256 = current.CertificateSha256,
                HasController = current.HasController,
                ConnectionRequestsEnabled = current.ConnectionRequestsEnabled,
                ConnectionRequestsExpiresUtc = current.ConnectionRequestsExpiresUtc
            });
        }

        IResult Pair(PairingRequest request)
        {
            if (request.ControllerId == Guid.Empty || string.IsNullOrWhiteSpace(request.ControllerCertificateBase64) ||
                request.ControllerCertificateBase64.Length > 16 * 1024)
                return Results.Json(new PairingResponse
                {
                    ErrorCode = "InvalidControllerCertificate",
                    Message = "Certificato del Controller mancante o non valido."
                }, statusCode: StatusCodes.Status400BadRequest);
            if (string.IsNullOrWhiteSpace(request.ControllerName) || request.ControllerName.Length > 128)
                return Results.Json(new PairingResponse
                {
                    ErrorCode = "InvalidControllerName",
                    Message = "Nome Controller non valido."
                }, statusCode: StatusCodes.Status400BadRequest);
            System.Security.Cryptography.X509Certificates.X509Certificate2 controllerCertificate;
            try
            {
                controllerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                    Convert.FromBase64String(request.ControllerCertificateBase64));
                using var publicKey = controllerCertificate.GetRSAPublicKey();
                if (publicKey is null || controllerCertificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
                    throw new System.Security.Cryptography.CryptographicException();
            }
            catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
            {
                return Results.Json(new PairingResponse
                {
                    ErrorCode = "InvalidControllerCertificate",
                    Message = "Certificato pubblico del Controller non valido."
                }, statusCode: StatusCodes.Status400BadRequest);
            }
            using (controllerCertificate)
            {
                if (!pairingCodes.TryConsume(request.Code))
                    return Results.Json(new PairingResponse
                    {
                        ErrorCode = "InvalidPairingCode",
                        Message = "Codice pairing non valido o scaduto."
                    }, statusCode: StatusCodes.Status403Forbidden);

                try
                {
                    settingsStore.PairController(request.ControllerName, controllerCertificate);
                    connectionRequests.ClearPending();
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Json(new PairingResponse
                    {
                        ErrorCode = "ControllerAlreadyPaired",
                        Message = ex.Message
                    }, statusCode: StatusCodes.Status409Conflict);
                }
            }

            var current = settingsStore.GetConfiguration();
            return Results.Json(new PairingResponse
            {
                Success = true,
                Message = "Controller associato.",
                AgentId = current.AgentId,
                AgentCertificateSha256 = current.CertificateSha256
            });
        }

        IResult CreateConnectionRequest(HttpContext context, ConnectionRequestCreate request)
        {
            var remoteAddress = context.Connection.RemoteIpAddress?.ToString() ?? "sconosciuto";
            var response = connectionRequests.Create(request, remoteAddress);
            var status = response.Success ? StatusCodes.Status202Accepted : response.ErrorCode switch
            {
                "RequestsDisabled" => StatusCodes.Status403Forbidden,
                "ControllerAlreadyPaired" or "RequestAlreadyPending" => StatusCodes.Status409Conflict,
                "RateLimited" => StatusCodes.Status429TooManyRequests,
                "TooManyRequests" => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status400BadRequest
            };
            return Results.Json(response, statusCode: status);
        }

        IResult ConnectionRequestStatus(Guid requestId, ConnectionRequestStatusQuery query)
        {
            var response = connectionRequests.GetStatus(requestId, query.PollToken);
            var status = response.Success ? StatusCodes.Status200OK : response.ErrorCode switch
            {
                "Unauthorized" => StatusCodes.Status403Forbidden,
                "RequestNotFound" => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status400BadRequest
            };
            return Results.Json(response, statusCode: status);
        }

        async Task<IResult> StatusAsync(HttpContext context)
        {
            var response = await SendLocalAsync(new AgentRequest { Command = AgentCommands.GetStatus }, stoppingToken)
                .ConfigureAwait(false);
            return ToHttpResult(response);
        }

        async Task<IResult> StartScanAsync(HttpContext context, ScanRequest? request)
        {
            var response = await SendLocalAsync(new AgentRequest
            {
                Command = AgentCommands.StartScan,
                Scan = request ?? new ScanRequest()
            }, stoppingToken).ConfigureAwait(false);
            return ToHttpResult(response, response.Success ? StatusCodes.Status202Accepted : null);
        }

        async Task<IResult> GetOperationAsync(HttpContext context, Guid operationId)
        {
            var response = await SendLocalAsync(new AgentRequest
            {
                Command = AgentCommands.GetOperation,
                OperationId = operationId
            }, stoppingToken).ConfigureAwait(false);
            return ToHttpResult(response);
        }

        async Task<IResult> StartUpdateAsync(HttpContext context, RemoteUpdateRequest? request)
        {
            var response = await SendLocalAsync(new AgentRequest
            {
                Command = AgentCommands.StartUpdate,
                Update = request
            }, stoppingToken).ConfigureAwait(false);
            return ToHttpResult(response, response.Success ? StatusCodes.Status202Accepted : null);
        }

        async Task<IResult> CancelOperationAsync(HttpContext context, Guid operationId)
        {
            var response = await SendLocalAsync(new AgentRequest
            {
                Command = AgentCommands.CancelOperation,
                OperationId = operationId
            }, stoppingToken).ConfigureAwait(false);
            return ToHttpResult(response);
        }

    }

    private static async Task<AgentResponse> SendLocalAsync(
        AgentRequest request,
        CancellationToken cancellationToken) =>
        await new AgentLocalClient().SendAsync(
            request,
            TimeSpan.FromSeconds(30),
            cancellationToken).ConfigureAwait(false);

    private static IResult ToHttpResult(AgentResponse response, int? successfulStatus = null)
    {
        if (response.Success)
            return Results.Json(response, statusCode: successfulStatus ?? StatusCodes.Status200OK);
        var status = response.ErrorCode switch
        {
            "AgentBusy" => StatusCodes.Status409Conflict,
            "OperationNotFound" => StatusCodes.Status404NotFound,
            "UpdateNotInScan" or "ScanNotAvailable" or "ScanExpired" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Json(response, statusCode: status);
    }
}
