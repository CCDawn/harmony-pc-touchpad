using System.Globalization;
using System.Text;
using HarmonyPcTouchpad.Agent.Core;
using HarmonyPcTouchpad.Agent.Protocol;

namespace HarmonyPcTouchpad.Agent.Transport;

public sealed class InputConnectionProcessor
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] ImplementedCapabilities =
        ["pointer-delta", "scroll-v1"];

    private readonly TimeProvider _clock;
    private readonly Func<string> _sessionIdFactory;
    private readonly Func<string> _messageIdFactory;

    public InputConnectionProcessor(
        TimeProvider clock,
        Func<string>? sessionIdFactory = null,
        Func<string>? messageIdFactory = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _sessionIdFactory = sessionIdFactory ??
            (() => $"session-{Guid.NewGuid():N}");
        _messageIdFactory = messageIdFactory ??
            (() => $"message-{Guid.NewGuid():N}");
    }

    public async Task RunAsync(
        string authenticatedDeviceId,
        ITransportConnection connection,
        InputSession session,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(authenticatedDeviceId);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(session);

        bool timedOut = false;
        TransportCloseReason? closeReason = null;
        session.Begin();
        try
        {
            TransportMessage helloMessage =
                await ReceiveAsync(connection, cancellationToken).ConfigureAwait(false);
            ClientHello hello =
                ControlMessageCodec.ReadHello(RequireText(helloMessage));
            if (hello.DeviceId != authenticatedDeviceId)
            {
                throw new ProtocolViolationException(
                    "HELLO device ID does not match the authenticated device.");
            }

            string[] negotiatedCapabilities = ImplementedCapabilities
                .Where(hello.Capabilities.Contains)
                .ToArray();
            if (negotiatedCapabilities.Length == 0)
            {
                throw new ProtocolViolationException(
                    "HELLO did not offer any implemented input capability.");
            }

            string sessionId = _sessionIdFactory();
            await connection.SendTextAsync(
                    ControlMessageCodec.CreateHelloAck(
                        sessionId,
                        _messageIdFactory(),
                        ReadMonotonicMicroseconds(),
                        negotiatedCapabilities),
                    cancellationToken)
                .ConfigureAwait(false);

            TransportMessage controlRequest =
                await ReceiveAsync(connection, cancellationToken).ConfigureAwait(false);
            ControlMessageCodec.RequireControlRequest(
                RequireText(controlRequest),
                sessionId);
            await connection.SendTextAsync(
                    ControlMessageCodec.CreateControlGranted(
                        sessionId,
                        authenticatedDeviceId,
                        _messageIdFactory(),
                        ReadMonotonicMicroseconds()),
                    cancellationToken)
                .ConfigureAwait(false);

            var inputLimiter = new InputRateLimiter(_clock);
            var controlLimiter = new InputRateLimiter(
                _clock,
                TransportPolicy.MaxControlRateHz);
            while (true)
            {
                TransportMessage message =
                    await ReceiveAsync(connection, cancellationToken).ConfigureAwait(false);
                if (message.Kind == TransportMessageKind.Closed)
                {
                    return;
                }

                if (message.Kind == TransportMessageKind.Binary)
                {
                    if (!inputLimiter.TryAccept())
                    {
                        throw new ProtocolViolationException(
                            "The negotiated input rate was exceeded.");
                    }

                    InputFrame frame = InputFrameDecoder.Decode(message.Payload.Span);
                    RequireNegotiatedCapability(frame, negotiatedCapabilities);
                    session.Process(frame);
                    continue;
                }

                if (message.Kind != TransportMessageKind.Text)
                {
                    throw new ProtocolViolationException(
                        "Unsupported WebSocket message type.");
                }

                if (!controlLimiter.TryAccept())
                {
                    throw new ProtocolViolationException(
                        "The control-message rate was exceeded.");
                }

                string text = ReadText(message.Payload);
                if (!ControlMessageCodec.TryReadHeartbeat(
                        text,
                        sessionId,
                        out string? pongNonce))
                {
                    throw new ProtocolViolationException(
                        "Only heartbeat control messages are allowed while controlling.");
                }

                if (pongNonce is not null)
                {
                    await connection.SendTextAsync(
                            ControlMessageCodec.CreatePong(
                                sessionId,
                                pongNonce,
                                _messageIdFactory(),
                                ReadMonotonicMicroseconds()),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (TimeoutException)
        {
            timedOut = true;
            closeReason = TransportCloseReason.PolicyViolation;
            throw;
        }
        catch (ProtocolViolationException)
        {
            closeReason = TransportCloseReason.ProtocolViolation;
            throw;
        }
        finally
        {
            try
            {
                if (timedOut)
                {
                    session.Timeout();
                }
                else
                {
                    session.Disconnect();
                }
            }
            finally
            {
                if (closeReason is not null)
                {
                    await TryCloseAsync(
                            connection,
                            closeReason.Value,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
    }

    private static async ValueTask<TransportMessage> ReceiveAsync(
        ITransportConnection connection,
        CancellationToken cancellationToken)
    {
        TransportMessage message = await connection.ReceiveAsync(
                TransportPolicy.MaxMessageBytes,
                TransportPolicy.IdleReleaseTimeout,
                cancellationToken)
            .ConfigureAwait(false);
        if (message.Payload.Length > TransportPolicy.MaxMessageBytes)
        {
            throw new ProtocolViolationException("WebSocket message is too large.");
        }

        return message;
    }

    private static string RequireText(TransportMessage message)
    {
        if (message.Kind != TransportMessageKind.Text)
        {
            throw new ProtocolViolationException("A text control message was required.");
        }

        return ReadText(message.Payload);
    }

    private static void RequireNegotiatedCapability(
        InputFrame frame,
        IReadOnlyList<string> negotiatedCapabilities)
    {
        string? requiredCapability = frame switch
        {
            PointerDeltaFrame => "pointer-delta",
            ScrollFrame => "scroll-v1",
            GestureFrame => "gesture-v1",
            _ => null
        };
        if (requiredCapability is not null &&
            !negotiatedCapabilities.Contains(requiredCapability))
        {
            throw new ProtocolViolationException(
                $"{frame.Type} was not negotiated for this input session.");
        }
    }

    private static string ReadText(ReadOnlyMemory<byte> payload)
    {
        try
        {
            return StrictUtf8.GetString(payload.Span);
        }
        catch (DecoderFallbackException error)
        {
            throw new ProtocolViolationException(
                "Control message is not valid UTF-8.",
                error);
        }
    }

    private string ReadMonotonicMicroseconds()
    {
        double microseconds =
            _clock.GetTimestamp() * (1_000_000d / _clock.TimestampFrequency);
        return checked((ulong)microseconds).ToString(CultureInfo.InvariantCulture);
    }

    private static async ValueTask TryCloseAsync(
        ITransportConnection connection,
        TransportCloseReason reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await connection.CloseAsync(reason, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Input release in the caller's finally block remains authoritative.
        }
    }
}
