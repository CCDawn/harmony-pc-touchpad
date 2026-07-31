using System.Net;

namespace HarmonyPcTouchpad.Agent.App.Tests;

public sealed class PairingEndpointSelectorTests
{
    [Fact]
    public void Create_UsesReachablePrivateIpv4Address()
    {
        Uri endpoint = PairingEndpointSelector.Create(
            [
                IPAddress.Parse("fd00::18"),
                IPAddress.Parse("192.168.20.18")
            ],
            47431);

        Assert.Equal(
            "wss://192.168.20.18:47431/pair",
            endpoint.AbsoluteUri);
    }

    [Fact]
    public void Create_FormatsIpv6Address()
    {
        Uri endpoint = PairingEndpointSelector.Create(
            [IPAddress.Parse("fd00::18")],
            47431);

        Assert.Equal(
            "wss://[fd00::18]:47431/pair",
            endpoint.AbsoluteUri);
    }

    [Fact]
    public void Create_RejectsEmptyAddressSet()
    {
        Assert.Throws<ArgumentException>(
            () => PairingEndpointSelector.Create([], 47431));
    }
}
