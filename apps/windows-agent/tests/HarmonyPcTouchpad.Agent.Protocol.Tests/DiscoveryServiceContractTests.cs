using System.Text.Json;

namespace HarmonyPcTouchpad.Agent.Protocol.Tests;

public sealed class DiscoveryServiceContractTests
{
    [Fact]
    public void MdnsIdentityIsFrozenForCrossRuntimeDiscovery()
    {
        Assert.Equal("_hptouchpad._tcp", DiscoveryServiceContract.ServiceType);
        Assert.Equal(
            "_hptouchpad._tcp.local",
            DiscoveryServiceContract.QualifiedServiceType);
        Assert.Equal(47431, DiscoveryServiceContract.Port);
        Assert.Equal(
            ["v", "id", "name", "pairing"],
            DiscoveryServiceContract.TxtKeys);
    }

    [Fact]
    public void CSharpConstantsMatchTheSharedDiscoveryFixture()
    {
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "discovery-service.json");
        using JsonDocument fixture =
            JsonDocument.Parse(File.ReadAllText(fixturePath));
        JsonElement root = fixture.RootElement;

        Assert.Equal(
            DiscoveryServiceContract.ServiceType,
            root.GetProperty("serviceType").GetString());
        Assert.Equal(
            DiscoveryServiceContract.QualifiedServiceType,
            root.GetProperty("qualifiedServiceType").GetString());
        Assert.Equal(
            DiscoveryServiceContract.Port,
            root.GetProperty("port").GetUInt16());
        Assert.Equal(
            DiscoveryServiceContract.TxtKeys,
            root.GetProperty("txtKeys")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());
    }
}
