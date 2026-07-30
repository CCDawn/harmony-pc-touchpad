namespace HarmonyPcTouchpad.Agent.App;

internal sealed record AgentStartupOptions(bool ShowPairing)
{
    public static AgentStartupOptions Parse(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        bool showPairing = arguments.Any(argument =>
            string.Equals(
                argument,
                "--show-pairing",
                StringComparison.OrdinalIgnoreCase));
        return new(showPairing);
    }
}
