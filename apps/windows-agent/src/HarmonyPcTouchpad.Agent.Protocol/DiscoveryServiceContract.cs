namespace HarmonyPcTouchpad.Agent.Protocol;

public static class DiscoveryServiceContract
{
    public const string ServiceType = "_hptouchpad._tcp";
    public const string QualifiedServiceType = "_hptouchpad._tcp.local";
    public const int Port = 47431;

    public static IReadOnlyList<string> TxtKeys { get; } =
        Array.AsReadOnly(["v", "id", "name", "pairing"]);
}
