using HarmonyPcTouchpad.Agent.App;

namespace HarmonyPcTouchpad.Agent.App.Tests;

public sealed class AgentStartupOptionsTests
{
    [Fact]
    public void Parse_EnablesPairingWindowForExplicitFlag()
    {
        AgentStartupOptions options =
            AgentStartupOptions.Parse(["--show-pairing"]);

        Assert.True(options.ShowPairing);
    }

    [Fact]
    public void Parse_IsCaseInsensitive()
    {
        AgentStartupOptions options =
            AgentStartupOptions.Parse(["--SHOW-PAIRING"]);

        Assert.True(options.ShowPairing);
    }

    [Fact]
    public void Parse_KeepsDefaultStartupForOtherArguments()
    {
        AgentStartupOptions options =
            AgentStartupOptions.Parse(["--unknown"]);

        Assert.False(options.ShowPairing);
    }
}
