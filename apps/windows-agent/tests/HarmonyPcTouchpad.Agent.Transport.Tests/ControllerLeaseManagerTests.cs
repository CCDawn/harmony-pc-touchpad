namespace HarmonyPcTouchpad.Agent.Transport.Tests;

public sealed class ControllerLeaseManagerTests
{
    [Fact]
    public void ExactlyOneControllerOwnsTheLeaseUntilItDisconnects()
    {
        var manager = new ControllerLeaseManager();

        Assert.True(manager.TryAcquire("phone-a", out ControllerLease? first));
        Assert.NotNull(first);
        Assert.False(manager.TryAcquire("phone-b", out ControllerLease? denied));
        Assert.Null(denied);

        first.Dispose();

        Assert.True(manager.TryAcquire("phone-b", out ControllerLease? second));
        Assert.NotNull(second);
        Assert.Equal("phone-b", second.DeviceId);
        second.Dispose();
    }

    [Fact]
    public void StaleLeaseCannotReleaseANewerController()
    {
        var manager = new ControllerLeaseManager();
        Assert.True(manager.TryAcquire("phone-a", out ControllerLease? first));
        Assert.NotNull(first);

        first.Dispose();
        Assert.True(manager.TryAcquire("phone-b", out ControllerLease? second));
        Assert.NotNull(second);

        first.Dispose();

        Assert.Equal("phone-b", manager.ActiveDeviceId);
        second.Dispose();
    }
}
