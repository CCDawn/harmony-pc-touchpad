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
            using var idleCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            idleCancellation.CancelAfter(idleTimeout);

            int length = 0;
            WebSocketMessageType? messageType = null;
            while (true)
            {
                ValueWebSocketReceiveResult result;
                try
                {
                    result = await _webSocket.ReceiveAsync(
                            buffer.AsMemory(length, maxMessageBytes + 1 - length),
                            idleCancellation.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException error)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        "The input connection exceeded the idle-release timeout.",
                        error);
                }

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
        await _webSocket.CloseOutputAsync(
                status,
                reason.ToString(),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
