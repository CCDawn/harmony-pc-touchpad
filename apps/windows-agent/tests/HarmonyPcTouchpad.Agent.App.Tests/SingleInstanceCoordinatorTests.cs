namespace HarmonyPcTouchpad.Agent.App.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public void SecondaryLaunchSignalsTheExistingPrimaryInstance()
    {
        string instanceId = $"HarmonyPcTouchpad.Tests.{Guid.NewGuid():N}";
        using var primary = new SingleInstanceCoordinator(instanceId);
        using var secondary = new SingleInstanceCoordinator(instanceId);
        using var activated = new ManualResetEventSlim();

        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);

        primary.StartListening(activated.Set);
        secondary.SignalPrimary();

        Assert.True(activated.Wait(TimeSpan.FromSeconds(2)));
    }
}
