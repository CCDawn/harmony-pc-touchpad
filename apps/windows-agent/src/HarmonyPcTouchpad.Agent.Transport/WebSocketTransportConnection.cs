using System.Buffers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using HarmonyPcTouchpad.Agent.Protocol;

namespace HarmonyPcTouchpad.Agent.Transport;

internal sealed class WebSocketTransportConnection(WebSocket webSocket) :
    ITransportConnection
{
    private readonly WebSocket _webSocket =
        webSocket ?? throw new ArgumentNullException(nameof(webSocket));

    public async ValueTask<TransportMessage> ReceiveAsync(
        int maxMessageBytes,
        TimeSpan idleTimeout,
        CancellationToken cancellationToken)
    {
        if (maxMessageBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMessageBytes));
        }

        if (idleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleTimeout));
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(maxMessageBytes + 1);
        try
        {
            int length = 0;
            WebSocketMessageType? messageType = null;
            Task idleDelay = Task.Delay(idleTimeout);
            while (true)
            {
                Task<ValueWebSocketReceiveResult> pendingReceive =
                    _webSocket.ReceiveAsync(
                            buffer.AsMemory(
                                length,
                                maxMessageBytes + 1 - length),
                            cancellationToken)
                        .AsTask();
                if (await Task.WhenAny(pendingReceive, idleDelay)
                        .ConfigureAwait(false) == idleDelay)
                {
                    await CloseAfterIdleTimeoutAsync(
                            pendingReceive,
                            cancellationToken)
                        .ConfigureAwait(false);
                    throw new TimeoutException(
                        "The input connection exceeded the idle-release timeout.");
                }

                ValueWebSocketReceiveResult result =
                    await pendingReceive.ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return TransportMessage.Closed();
                }

                if (messageType is not null && messageType != result.MessageType)
                {
                    throw new ProtocolViolationException(
                        "A fragmented WebSocket message changed its message type.");
                }

                messageType = result.MessageType;
                length += result.Count;
                if (length > maxMessageBytes)
                {
                    throw new ProtocolViolationException(
                        "WebSocket message exceeds the configured size limit.");
                }

                if (!result.EndOfMessage)
                {
                    continue;
                }

                byte[] payload = buffer.AsSpan(0, length).ToArray();
                return result.MessageType switch
                {
                    WebSocketMessageType.Text =>
                        new(TransportMessageKind.Text, payload),
                    WebSocketMessageType.Binary =>
                        new(TransportMessageKind.Binary, payload),
                    _ => throw new ProtocolViolationException(
                        "Unsupported WebSocket message type.")
                };
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private async ValueTask CloseAfterIdleTimeoutAsync(
        Task<ValueWebSocketReceiveResult> pendingReceive,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_webSocket.State is WebSocketState.Open)
            {
                await _webSocket.CloseOutputAsync(
                        WebSocketCloseStatus.PolicyViolation,
                        TransportCloseReason.PolicyViolation.ToString(),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            using var closeCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            closeCancellation.CancelAfter(TransportPolicy.CloseHandshakeTimeout);
            ValueWebSocketReceiveResult closeResult =
                await pendingReceive.WaitAsync(closeCancellation.Token)
                    .ConfigureAwait(false);
            if (closeResult.MessageType != WebSocketMessageType.Close)
            {
                _webSocket.Abort();
            }
        }
        catch
        {
            _webSocket.Abort();
            try
            {
                await pendingReceive.ConfigureAwait(false);
            }
            catch
            {
                // Abort is the final fail-closed outcome for the pending receive.
            }
        }
    }

    public async ValueTask SendTextAsync(
        string text,
        CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        try
        {
            await _webSocket.SendAsync(
                    bytes.AsMemory(),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public async ValueTask CloseAsync(
        TransportCloseReason reason,
        CancellationToken cancellationToken)
    {
        if (_webSocket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        WebSocketCloseStatus status = reason switch
        {
            TransportCloseReason.Normal => WebSocketCloseStatus.NormalClosure,
            TransportCloseReason.ProtocolViolation =>
                WebSocketCloseStatus.InvalidPayloadData,
            TransportCloseReason.PolicyViolation =>
                WebSocketCloseStatus.PolicyViolation,
            TransportCloseReason.ServerShutdown =>
                WebSocketCloseStatus.EndpointUnavailable,
            _ => WebSocketCloseStatus.InternalServerError
        };
        using var closeCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        closeCancellation.CancelAfter(TransportPolicy.CloseHandshakeTimeout);
        await _webSocket.CloseAsync(
                status,
                reason.ToString(),
                closeCancellation.Token)
            .ConfigureAwait(false);
    }
}
