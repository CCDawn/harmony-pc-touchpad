using System.Text;

namespace HarmonyPcTouchpad.Agent.Transport;

public enum TransportMessageKind
{
    Text,
    Binary,
    Closed
}

public enum TransportCloseReason
{
    Normal,
    ProtocolViolation,
    PolicyViolation,
    ServerShutdown
}

public sealed record TransportMessage(
    TransportMessageKind Kind,
    ReadOnlyMemory<byte> Payload)
{
    public static TransportMessage Text(string text) =>
        new(TransportMessageKind.Text, Encoding.UTF8.GetBytes(text));

    public static TransportMessage Binary(byte[] bytes) =>
        new(TransportMessageKind.Binary, bytes);

    public static TransportMessage Closed() =>
        new(TransportMessageKind.Closed, ReadOnlyMemory<byte>.Empty);
}

public interface ITransportConnection
{
    ValueTask<TransportMessage> ReceiveAsync(
        int maxMessageBytes,
        TimeSpan idleTimeout,
        CancellationToken cancellationToken);

    ValueTask SendTextAsync(string text, CancellationToken cancellationToken);

    ValueTask CloseAsync(
        TransportCloseReason reason,
        CancellationToken cancellationToken);
}
