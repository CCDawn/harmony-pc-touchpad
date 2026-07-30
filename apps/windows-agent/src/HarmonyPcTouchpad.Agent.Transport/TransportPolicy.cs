namespace HarmonyPcTouchpad.Agent.Transport;

public static class TransportPolicy
{
    public const int Port = 47431;
    public const int MaxMessageBytes = 4096;
    public const int MaxInputRateHz = 120;
    internal const int MaxControlRateHz = 8;
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan IdleReleaseTimeout = TimeSpan.FromMilliseconds(1000);
    internal static readonly TimeSpan CloseHandshakeTimeout =
        TimeSpan.FromMilliseconds(1000);
}
