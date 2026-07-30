using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using HarmonyPcTouchpad.Agent.Core;
using HarmonyPcTouchpad.Agent.Protocol;
using HarmonyPcTouchpad.Agent.Security;
using Microsoft.AspNetCore.Http;

namespace HarmonyPcTouchpad.Agent.Transport;

internal static class AgentWebSocketEndpoints
{
    public static async Task HandlePairingAsync(
        HttpContext context,
        PairingAuthority pairingAuthority,
        TimeProvider clock)
    {
        if (!RequireWebSocketGet(context))
        {
            return;
        }

        if (!UpgradeHeaderParser.TryReadPairing(
                context.Request.Headers,
                out PairingUpgradeHeaders? request) ||
            request is null)
        {
            RejectAuthentication(context);
            return;
        }

        IssuedDeviceCredential credential;
        try
        {
            credential = pairingAuthority.Complete(
                request.PairingToken,
                request.DeviceId);
        }
        catch (PairingRejectedException)
        {
            RejectAuthentication(context);
            return;
        }

        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync(
            new WebSocketAcceptContext
            {
                DangerousEnableCompression = false
            });
        string response = ControlMessageCodec.CreatePairingAccepted(
            credential.DeviceId,
            credential.DeviceSecret,
            $"message-{Guid.NewGuid():N}",
            ReadMonotonicMicroseconds(clock));
        byte[] responseBytes = Encoding.UTF8.GetBytes(response);
        try
        {
            await socket.SendAsync(
                    responseBytes,
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    context.RequestAborted)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(responseBytes);
        }

        await socket.CloseOutputAsync(
                WebSocketCloseStatus.NormalClosure,
                "Pairing completed.",
                context.RequestAborted)
            .ConfigureAwait(false);
    }

    public static async Task HandleInputAsync(
        HttpContext context,
        RequestAuthenticator authenticator,
        ControllerLeaseManager leases,
        InputConnectionProcessor processor,
        IInputSink inputSink)
    {
        if (!RequireWebSocketGet(context))
        {
            return;
        }

        if (!UpgradeHeaderParser.TryReadInput(
                context.Request.Headers,
                out AuthRequest? request) ||
            request is null ||
            !authenticator.TryAuthenticate(request))
        {
            RejectAuthentication(context);
            return;
        }

        if (!leases.TryAcquire(request.DeviceId, out ControllerLease? lease) ||
            lease is null)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        using (lease)
        using (WebSocket socket = await context.WebSockets.AcceptWebSocketAsync(
                   new WebSocketAcceptContext
                   {
                       DangerousEnableCompression = false
                   }))
        {
            var connection = new WebSocketTransportConnection(socket);
            try
            {
                await processor.RunAsync(
                        request.DeviceId,
                        connection,
                        new InputSession(inputSink),
                        context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (Exception error)
                when (error is ProtocolViolationException or
                    TimeoutException or
                    WebSocketException or
                    OperationCanceledException)
            {
                // The processor releases all input and closes the socket fail-closed.
            }
        }
    }

    private static bool RequireWebSocketGet(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return false;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return false;
        }

        return true;
    }

    private static void RejectAuthentication(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    private static string ReadMonotonicMicroseconds(TimeProvider clock)
    {
        double microseconds =
            clock.GetTimestamp() * (1_000_000d / clock.TimestampFrequency);
        return checked((ulong)microseconds).ToString(CultureInfo.InvariantCulture);
    }
}
