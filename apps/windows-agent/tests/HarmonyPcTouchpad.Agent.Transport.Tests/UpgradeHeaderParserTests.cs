using HarmonyPcTouchpad.Agent.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace HarmonyPcTouchpad.Agent.Transport.Tests;

public sealed class UpgradeHeaderParserTests
{
    [Fact]
    public void PairingHeadersRequireSingleExactValues()
    {
        var headers = new HeaderDictionary
        {
            ["X-HPT-Version"] = "1",
            ["X-HPT-Device-Id"] = "phone-001",
            ["X-HPT-Pairing-Token"] = Base64Url.Encode(new byte[32])
        };

        Assert.True(UpgradeHeaderParser.TryReadPairing(headers, out PairingUpgradeHeaders? parsed));
        Assert.NotNull(parsed);
        Assert.Equal("phone-001", parsed.DeviceId);

        headers["X-HPT-Version"] = new StringValues(["1", "1"]);
        Assert.False(UpgradeHeaderParser.TryReadPairing(headers, out _));
    }

    [Fact]
    public void InputHeadersBuildTheFrozenAuthenticationRequest()
    {
        var headers = new HeaderDictionary
        {
            ["X-HPT-Version"] = "1",
            ["X-HPT-Device-Id"] = "phone-001",
            ["X-HPT-Timestamp-Unix-Ms"] = "1775000000000",
            ["X-HPT-Nonce"] = Base64Url.Encode(new byte[16]),
            ["X-HPT-Signature"] = Base64Url.Encode(new byte[32])
        };

        Assert.True(UpgradeHeaderParser.TryReadInput(headers, out AuthRequest? request));
        Assert.Equal("GET", request?.Method);
        Assert.Equal("/input", request?.Path);
        Assert.Equal(1775000000000, request?.TimestampUnixMs);

        headers["X-HPT-Timestamp-Unix-Ms"] = "+1775000000000";
        Assert.False(UpgradeHeaderParser.TryReadInput(headers, out _));
    }
}
