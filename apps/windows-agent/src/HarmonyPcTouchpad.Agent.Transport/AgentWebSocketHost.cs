using System.Net;
using System.Security.Cryptography.X509Certificates;
using HarmonyPcTouchpad.Agent.Core;
using HarmonyPcTouchpad.Agent.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HarmonyPcTouchpad.Agent.Transport;

public sealed class AgentWebSocketHost : IAsyncDisposable
{
    private readonly WebApplication _application;

    public AgentWebSocketHost(
        string agentId,
        X509Certificate2 certificate,
        IEnumerable<IPAddress> listenAddresses,
        PairingAuthority pairingAuthority,
        RequestAuthenticator authenticator,
        IInputSink inputSink,
        TimeProvider? clock = null)
    {
        if (!SecurityIdentifiers.IsValid(agentId))
        {
            throw new ArgumentException("Agent ID is invalid.", nameof(agentId));
        }

        ArgumentNullException.ThrowIfNull(certificate);
        if (!certificate.HasPrivateKey)
        {
            throw new ArgumentException(
                "The TLS certificate must contain a private key.",
                nameof(certificate));
        }

        IPAddress[] addresses = (listenAddresses ??
                throw new ArgumentNullException(nameof(listenAddresses)))
            .Distinct()
            .ToArray();
        if (addresses.Length == 0 ||
            addresses.Any(address => !PrivateNetworkAddressPolicy.IsAllowed(address)))
        {
            throw new ArgumentException(
                "At least one private LAN address is required and every binding must be private.",
                nameof(listenAddresses));
        }

        ArgumentNullException.ThrowIfNull(pairingAuthority);
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(inputSink);
        TimeProvider timeProvider = clock ?? TimeProvider.System;

        var leases = new ControllerLeaseManager();
        var processor = new InputConnectionProcessor(timeProvider);
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(AgentWebSocketHost).Assembly.FullName
            });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            foreach (IPAddress address in addresses)
            {
                options.Listen(
                    address,
                    TransportPolicy.Port,
                    listen =>
                    {
                        listen.Protocols = HttpProtocols.Http1;
                        listen.UseHttps(certificate);
                    });
            }
        });

        _application = builder.Build();
        _application.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = Timeout.InfiniteTimeSpan
        });
        _application.Map(
            "/pair",
            context => AgentWebSocketEndpoints.HandlePairingAsync(
                context,
                pairingAuthority,
                timeProvider));
        _application.Map(
            "/input",
            context => AgentWebSocketEndpoints.HandleInputAsync(
                context,
                authenticator,
                leases,
                processor,
                inputSink));
        _application.MapFallback(context =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _application.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _application.StopAsync(cancellationToken);

    public ValueTask DisposeAsync() => _application.DisposeAsync();
}
