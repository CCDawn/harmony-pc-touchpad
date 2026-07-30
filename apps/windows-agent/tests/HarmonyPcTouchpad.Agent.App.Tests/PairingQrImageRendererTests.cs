using System.Buffers.Binary;
using HarmonyPcTouchpad.Agent.App;

namespace HarmonyPcTouchpad.Agent.App.Tests;

public sealed class PairingQrImageRendererTests
{
    [Fact]
    public void RenderPng_ProducesSquarePngWithQuietZone()
    {
        byte[] png = PairingQrImageRenderer.RenderPng(
            "{\"v\":1,\"agentId\":\"agent-001\"}",
            pixelsPerModule: 8);

        Assert.True(png.Length > 256);
        Assert.Equal(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
            png[..8]);
        int width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        Assert.Equal(width, height);
        Assert.True(width >= 200);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RenderPng_RejectsBlankPayload(string payload)
    {
        Assert.Throws<ArgumentException>(
            () => PairingQrImageRenderer.RenderPng(payload, 8));
    }

    [Fact]
    public void RenderPng_RejectsInvalidScale()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PairingQrImageRenderer.RenderPng("payload", 0));
    }
}
