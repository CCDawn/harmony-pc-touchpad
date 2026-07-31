using System.Buffers.Binary;
using System.Text.Json;
using HarmonyPcTouchpad.Agent.Core;
using HarmonyPcTouchpad.Agent.Protocol;

namespace HarmonyPcTouchpad.Agent.Transport.Tests;

public sealed class InputConnectionProcessorTests
{
    [Fact]
    public async Task HandshakeDispatchAndDisconnectReleaseAllInput()
    {
        var connection = new ScriptedConnection(
            TransportMessage.Text(Hello("phone-001")),
            TransportMessage.Text(ControlRequest("session-test")),
            TransportMessage.Binary(PointerFrame(sequence: 0)),
            TransportMessage.Closed());
        var sink = new RecordingInputSink();
        var processor = new InputConnectionProcessor(
            TimeProvider.System,
            () => "session-test",
            () => "message-test");

        await processor.RunAsync(
            "phone-001",
            connection,
            new InputSession(sink),
            CancellationToken.None);

        Assert.Equal(["Move:2:-3", "ReleaseAll"], sink.Events);
        Assert.Equal(["HELLO_ACK", "CONTROL_GRANTED"], connection.SentKinds);
        Assert.All(
            connection.ReceivePolicies,
            policy =>
            {
                Assert.Equal(TransportPolicy.MaxMessageBytes, policy.MaxMessageBytes);
                Assert.Equal(TransportPolicy.IdleReleaseTimeout, policy.IdleTimeout);
            });
    }

    [Fact]
    public async Task MissingHeartbeatTimesOutAndReleasesAllInput()
    {
        var sink = new RecordingInputSink();
        var connection = new ScriptedConnection(
            TransportMessage.Text(Hello("phone-001")),
            TransportMessage.Text(ControlRequest("session-test")),
            new TimeoutException("Synthetic idle timeout."))
        {
            ReleaseObserved = () => sink.Events.Contains("ReleaseAll")
        };
        var processor = new InputConnectionProcessor(
            TimeProvider.System,
            () => "session-test",
            () => "message-test");

        await Assert.ThrowsAsync<TimeoutException>(() => processor.RunAsync(
            "phone-001",
            connection,
            new InputSession(sink),
            CancellationToken.None));

        Assert.Equal(["ReleaseAll"], sink.Events);
        Assert.Equal(TransportCloseReason.PolicyViolation, connection.CloseReason);
        Assert.True(connection.ReleasedBeforeClose);
    }

    [Fact]
    public async Task HelloCannotClaimADifferentAuthenticatedDevice()
    {
        var connection = new ScriptedConnection(
            TransportMessage.Text(Hello("phone-b")));
        var sink = new RecordingInputSink();
        var processor = new InputConnectionProcessor(
            TimeProvider.System,
            () => "session-test",
            () => "message-test");

        await Assert.ThrowsAsync<ProtocolViolationException>(() => processor.RunAsync(
            "phone-a",
            connection,
            new InputSession(sink),
            CancellationToken.None));

        Assert.Equal(["ReleaseAll"], sink.Events);
        Assert.Equal(TransportCloseReason.ProtocolViolation, connection.CloseReason);
    }

    [Fact]
    public async Task HeartbeatDoesNotConsumeTheNegotiatedBinaryInputBudget()
    {
        var script = new List<object>
        {
            TransportMessage.Text(Hello("phone-001")),
            TransportMessage.Text(ControlRequest("session-test"))
        };
        script.AddRange(
            Enumerable.Range(0, TransportPolicy.MaxInputRateHz)
                .Select(sequence =>
                    (object)TransportMessage.Binary(PointerFrame((uint)sequence))));
        script.Add(TransportMessage.Text(Ping("session-test")));
        script.Add(TransportMessage.Closed());
        var connection = new ScriptedConnection(script.ToArray());
        var sink = new RecordingInputSink();
        var processor = new InputConnectionProcessor(
            TimeProvider.System,
            () => "session-test",
            () => "message-test");

        await processor.RunAsync(
            "phone-001",
            connection,
            new InputSession(sink),
            CancellationToken.None);

        Assert.Equal(TransportPolicy.MaxInputRateHz + 1, sink.Events.Count);
        Assert.Equal("PONG", connection.SentKinds.Last());
    }

    [Fact]
    public async Task HelloAckContainsOnlyCommonImplementedCapabilities()
    {
        var connection = new ScriptedConnection(
            TransportMessage.Text(Hello(
                "phone-001",
                ["pointer-delta", "scroll-v1", "gesture-v1", "future-capability"])),
            TransportMessage.Text(ControlRequest("session-test")),
            TransportMessage.Closed());
        var processor = new InputConnectionProcessor(
            TimeProvider.System,
            () => "session-test",
            () => "message-test");

        await processor.RunAsync(
            "phone-001",
            connection,
            new InputSession(new RecordingInputSink()),
            CancellationToken.None);

        using JsonDocument helloAck = JsonDocument.Parse(connection.SentTexts[0]);
        string[] capabilities = helloAck.RootElement
            .GetProperty("payload")
            .GetProperty("capabilities")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        Assert.Equal(["pointer-delta", "scroll-v1", "gesture-v1"], capabilities);
    }

    [Fact]
    public async Task UnsupportedMinorVersionIsRejectedBeforeControlIsGranted()
    {
        var connection = new ScriptedConnection(
            TransportMessage.Text(Hello(
                "phone-001",
                ["pointer-delta"],
                minorVersion: 1)));
        var processor = new InputConnectionProcessor(
            TimeProvider.System,
            () => "session-test",
            () => "message-test");

        await Assert.ThrowsAsync<ProtocolViolationException>(() =>
            processor.RunAsync(
                "phone-001",
                connection,
                new InputSession(new RecordingInputSink()),
                CancellationToken.None));

        Assert.Empty(connection.SentTexts);
        Assert.Equal(TransportCloseReason.ProtocolViolation, connection.CloseReason);
    }

    [Fact]
    public async Task BinaryFramesMustBelongToTheNegotiatedCapabilitySet()
    {
        var connection = new ScriptedConnection(
            TransportMessage.Text(Hello("phone-001", ["pointer-delta"])),
            TransportMessage.Text(ControlRequest("session-test")),
            TransportMessage.Binary(ScrollFrame(sequence: 0)));
        var sink = new RecordingInputSink();
        var processor = new InputConnectionProcessor(
            TimeProvider.System,
            () => "session-test",
            () => "message-test");

        await Assert.ThrowsAsync<ProtocolViolationException>(() =>
            processor.RunAsync(
                "phone-001",
                connection,
                new InputSession(sink),
                CancellationToken.None));

        Assert.Equal(["ReleaseAll"], sink.Events);
        Assert.Equal(TransportCloseReason.ProtocolViolation, connection.CloseReason);
    }

    private static string Hello(
        string deviceId,
        string[]? capabilities = null,
        int minorVersion = 0) =>
        JsonSerializer.Serialize(new
        {
            protocol = new { major = 1, minor = minorVersion },
            kind = "HELLO",
            messageId = "hello-1",
            sessionId = (string?)null,
            sentAtUs = "1",
            payload = new
            {
                deviceId,
                deviceName = "Harmony Phone",
                capabilities = capabilities ?? ["pointer-delta", "scroll-v1"]
            }
        });

    private static string ControlRequest(string sessionId) =>
        JsonSerializer.Serialize(new
        {
            protocol = new { major = 1, minor = 0 },
            kind = "CONTROL_REQUEST",
            messageId = "control-1",
            sessionId,
            sentAtUs = "2",
            payload = new { }
        });

    private static string Ping(string sessionId) =>
        JsonSerializer.Serialize(new
        {
            protocol = new { major = 1, minor = 0 },
            kind = "PING",
            messageId = "ping-1",
            sessionId,
            sentAtUs = "3",
            payload = new { nonce = "ping-1" }
        });

    private static byte[] PointerFrame(uint sequence)
    {
        byte[] frame = new byte[28];
        frame[0] = 1;
        frame[1] = (byte)InputFrameType.PointerDelta;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), sequence);
        BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(
            frame.AsSpan(16),
            BitConverter.SingleToInt32Bits(2));
        BinaryPrimitives.WriteInt32LittleEndian(
            frame.AsSpan(20),
            BitConverter.SingleToInt32Bits(-3));
        BinaryPrimitives.WriteInt32LittleEndian(
            frame.AsSpan(24),
            BitConverter.SingleToInt32Bits(5));
        return frame;
    }

    private static byte[] ScrollFrame(uint sequence)
    {
        byte[] frame = new byte[28];
        frame[0] = 1;
        frame[1] = (byte)InputFrameType.Scroll;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), sequence);
        BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(
            frame.AsSpan(16),
            BitConverter.SingleToInt32Bits(1));
        BinaryPrimitives.WriteInt32LittleEndian(
            frame.AsSpan(20),
            BitConverter.SingleToInt32Bits(1));
        frame[24] = (byte)InputPhase.Begin;
        return frame;
    }

    private sealed class ScriptedConnection(params object[] script) : ITransportConnection
    {
        private readonly Queue<object> _script = new(script);

        public List<string> SentKinds { get; } = [];

        public List<string> SentTexts { get; } = [];

        public List<(int MaxMessageBytes, TimeSpan IdleTimeout)> ReceivePolicies { get; } = [];

        public TransportCloseReason? CloseReason { get; private set; }

        public Func<bool>? ReleaseObserved { get; init; }

        public bool? ReleasedBeforeClose { get; private set; }

        public ValueTask<TransportMessage> ReceiveAsync(
            int maxMessageBytes,
            TimeSpan idleTimeout,
            CancellationToken cancellationToken)
        {
            ReceivePolicies.Add((maxMessageBytes, idleTimeout));
            object next = _script.Dequeue();
            return next is Exception error
                ? ValueTask.FromException<TransportMessage>(error)
                : ValueTask.FromResult((TransportMessage)next);
        }

        public ValueTask SendTextAsync(string text, CancellationToken cancellationToken)
        {
            SentTexts.Add(text);
            using JsonDocument document = JsonDocument.Parse(text);
            SentKinds.Add(document.RootElement.GetProperty("kind").GetString()!);
            return ValueTask.CompletedTask;
        }

        public ValueTask CloseAsync(
            TransportCloseReason reason,
            CancellationToken cancellationToken)
        {
            CloseReason = reason;
            ReleasedBeforeClose = ReleaseObserved?.Invoke();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingInputSink : IInputSink
    {
        public List<string> Events { get; } = [];

        public void MovePointer(PointerDeltaFrame frame) =>
            Events.Add($"Move:{frame.Dx}:{frame.Dy}");

        public void SetButton(InputButton button, ButtonAction action) =>
            Events.Add($"Button:{button}:{action}");

        public void Scroll(ScrollFrame frame) =>
            Events.Add($"Scroll:{frame.Dx}:{frame.Dy}");

        public void HandleGesture(GestureFrame frame) =>
            Events.Add($"Gesture:{frame.Gesture}");

        public void ReleaseAll() => Events.Add("ReleaseAll");
    }
}
