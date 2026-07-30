namespace HarmonyPcTouchpad.Agent.Protocol;

public sealed class ProtocolViolationException : Exception
{
    public ProtocolViolationException(string message)
        : base(message)
    {
    }

    public ProtocolViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
