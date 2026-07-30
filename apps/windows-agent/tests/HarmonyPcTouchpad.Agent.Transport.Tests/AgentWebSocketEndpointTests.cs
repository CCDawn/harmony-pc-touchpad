using System.Net.WebSockets;
using HarmonyPcTouchpad.Agent.Core;
using HarmonyPcTouchpad.Agent.Protocol;
using HarmonyPcTouchpad.Agent.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace HarmonyPcTouchpad.Agent.Transport.Tests;

public sealed class AgentWebSocketEndpointTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InvalidPairingRequestIsRejectedBeforeWebSocketUpgrade()
    {
        var feature = new RecordingWebSocketFeature();
        DefaultHttpContext context = CreateContext(feature);
        var credentials = new MemoryCredentialStore();
        var random = new SystemSecureRandom();
        var tickets = new PairingTicketService(
            new FixedTimeProvider(Now),
            random);
        var authority = new PairingAuthority(tickets, credentials, random);

        await AgentWebSocketEndpoints.HandlePairingAsync(
            context,
            authority,
            new FixedTimeProvider(Now));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal(0, feature.AcceptCalls);
    }

    [Fact]
    public async Task InvalidHmacIsRejectedBeforeWebSocketUpgrade()
    {
        var feature = new RecordingWebSocketFeature();
        DefaultHttpContext context = CreateContext(feature);
        var credentials = new MemoryCredentialStore();
        credentials.SaveSecret("phone-001", new byte[32]);
        AddInputHeaders(context, Base64Url.Encode(new byte[32]));
        var authenticator = CreateAuthenticator(credentials);

        await AgentWebSocketEndpoints.HandleInputAsync(
            context,
            authenticator,
            new ControllerLeaseManager(),
            new InputConnectionProcessor(new FixedTimeProvider(Now)),
            new NoopInputSink());

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal(0, feature.AcceptCalls);
    }

    [Fact]
    public async Task BusyControllerIsRejectedBeforeWebSocketUpgrade()
    {
        var feature = new RecordingWebSocketFeature();
        DefaultHttpContext context = CreateContext(feature);
        byte[] secret = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var credentials = new MemoryCredentialStore();
        credentials.SaveSecret("phone-001", secret);
        string nonce = Base64Url.Encode(new byte[16]);
        string signature = AuthSignature.Create(
            secret,
            "GET",
            "/input",
            "agent-001",
            "phone-001",
            Now.ToUnixTimeMilliseconds(),
            nonce);
        AddInputHeaders(context, signature);
        var leases = new ControllerLeaseManager();
        Assert.True(leases.TryAcquire("phone-busy", out ControllerLease? busy));

        await AgentWebSocketEndpoints.HandleInputAsync(
            context,
            CreateAuthenticator(credentials),
            leases,
            new InputConnectionProcessor(new FixedTimeProvider(Now)),
            new NoopInputSink());

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal(0, feature.AcceptCalls);
        busy?.Dispose();
    }

    private static DefaultHttpContext CreateContext(
        RecordingWebSocketFeature feature)
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(feature);
        context.Request.Method = "GET";
        return context;
    }

    private static void AddInputHeaders(
        DefaultHttpContext context,
        string signature)
    {
        context.Request.Headers["X-HPT-Version"] = "1";
        context.Request.Headers["X-HPT-Device-Id"] = "phone-001";
        context.Request.Headers["X-HPT-Timestamp-Unix-Ms"] =
            Now.ToUnixTimeMilliseconds().ToString();
        context.Request.Headers["X-HPT-Nonce"] = Base64Url.Encode(new byte[16]);
        context.Request.Headers["X-HPT-Signature"] = signature;
    }

    private static RequestAuthenticator CreateAuthenticator(
        IDeviceCredentialStore credentials) =>
        new(
            "agent-001",
            credentials,
            new FixedTimeProvider(Now),
            RequestAuthenticator.AllowedClockSkew,
            RequestAuthenticator.ReplayLifetime);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingWebSocketFeature : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;

        public int AcceptCalls { get; private set; }

        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context)
        {
            AcceptCalls++;
            throw new InvalidOperationException(
                "The test must reject before accepting a WebSocket.");
        }
    }

    private sealed class MemoryCredentialStore :
        IDeviceCredentialStore,
        IDeviceCredentialWriter
    {
        private readonly Dictionary<string, byte[]> _secrets = [];

        public void SaveSecret(string deviceId, ReadOnlySpan<byte> deviceSecret) =>
            _secrets[deviceId] = deviceSecret.ToArray();

        public bool TryGetSecret(string deviceId, out byte[] deviceSecret)
        {
            if (!_secrets.TryGetValue(deviceId, out byte[]? secret))
            {
                deviceSecret = [];
                return false;
            }

            deviceSecret = secret.ToArray();
            return true;
        }

        public bool DeleteSecret(string deviceId) => _secrets.Remove(deviceId);
    }

    private sealed class NoopInputSink : IInputSink
    {
        public void MovePointer(PointerDeltaFrame frame)
        {
        }

        public void SetButton(InputButton button, ButtonAction action)
        {
        }

        public void Scroll(ScrollFrame frame)
        {
        }

        public void HandleGesture(GestureFrame frame)
        {
        }

        public void ReleaseAll()
        {
        }
    }
}
