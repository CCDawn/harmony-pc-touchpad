using System.Net;

namespace HarmonyPcTouchpad.Agent.Transport.Tests;

public sealed class PrivateNetworkAddressPolicyTests
{
    [Theory]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.50.10")]
    [InlineData("fd12:3456:789a::1")]
    public void PrivateLanAddressesAreAllowed(string value) =>
        Assert.True(PrivateNetworkAddressPolicy.IsAllowed(IPAddress.Parse(value)));

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.1.1")]
    [InlineData("172.32.0.1")]
    [InlineData("8.8.8.8")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("2001:4860:4860::8888")]
    public void PublicWildcardLoopbackAndLinkLocalAddressesAreRejected(string value) =>
        Assert.False(PrivateNetworkAddressPolicy.IsAllowed(IPAddress.Parse(value)));
}
