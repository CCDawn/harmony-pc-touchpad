using System.Net.WebSockets;

namespace HarmonyPcTouchpad.Agent.Transport.Tests;

public sealed class WebSocketTransportConnectionTests
{
    [Fact]
    public async Task CloseAsyncCompletesTheWebSocketCloseHandshake()
    {
        var socket = new RecordingWebSocket();
        var connection = new WebSocketTransportConnection(socket);

        await connection.CloseAsync(
            TransportCloseReason.PolicyViolation,
            CancellationToken.None);

        Assert.Equal(1, socket.CloseCalls);
        Assert.Equal(0, socket.CloseOutputCalls);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, socket.CloseStatus);
    }

    [Fact]
    public async Task IdleTimeoutSendsCloseBeforeCancellingTheActiveReceive()
    {
        var socket = new RecordingWebSocket
        {
            AcknowledgeCloseOutput = true
        };
        var connection = new WebSocketTransportConnection(socket);

        await Assert.ThrowsAsync<TimeoutException>(
            () => connection.ReceiveAsync(
                    maxMessageBytes: 64,
                    TimeSpan.FromMilliseconds(10),
                    CancellationToken.None)
                .AsTask());

        Assert.False(socket.ReceiveWasCancelled);
        Assert.Equal(1, socket.CloseOutputCalls);
        Assert.Equal(0, socket.AbortCalls);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, socket.CloseStatus);
    }

    private sealed class RecordingWebSocket : WebSocket
    {
        private WebSocketCloseStatus? _closeStatus;
        private string? _closeStatusDescription;
        private WebSocketState _state = WebSocketState.Open;
        private TaskCompletionSource<ValueWebSocketReceiveResult>? _pendingReceive;

        public bool AcknowledgeCloseOutput { get; init; }

        public int CloseCalls { get; private set; }

        public int CloseOutputCalls { get; private set; }

        public int AbortCalls { get; private set; }

        public bool ReceiveWasCancelled { get; private set; }

        public override WebSocketCloseStatus? CloseStatus => _closeStatus;

        public override string? CloseStatusDescription => _closeStatusDescription;

        public override WebSocketState State => _state;

        public override string? SubProtocol => null;

        public override void Abort()
        {
            AbortCalls++;
            _state = WebSocketState.Aborted;
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            CloseCalls++;
            _closeStatus = closeStatus;
            _closeStatusDescription = statusDescription;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            CloseOutputCalls++;
            _closeStatus = closeStatus;
            _closeStatusDescription = statusDescription;
            _state = WebSocketState.CloseSent;
            if (AcknowledgeCloseOutput)
            {
                _state = WebSocketState.Closed;
                _pendingReceive?.TrySetResult(
                    new ValueWebSocketReceiveResult(
                        0,
                        WebSocketMessageType.Close,
                        endOfMessage: true));
            }

            return Task.CompletedTask;
        }

        public override void Dispose() => _state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken)
        {
            _pendingReceive =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() =>
            {
                ReceiveWasCancelled = true;
                _pendingReceive.TrySetCanceled(cancellationToken);
            });
            return new ValueTask<ValueWebSocketReceiveResult>(_pendingReceive.Task);
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
