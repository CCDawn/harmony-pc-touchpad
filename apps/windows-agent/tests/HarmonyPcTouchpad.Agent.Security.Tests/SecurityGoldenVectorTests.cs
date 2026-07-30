using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace HarmonyPcTouchpad.Agent.Security.Tests;

public sealed class SecurityGoldenVectorTests
{
    [Fact]
    public void SharedQrAndHmacVectorsMatchTheCSharpImplementation()
    {
        using JsonDocument fixture = LoadFixture();
        JsonElement qr = fixture.RootElement.GetProperty("pairingQr");
        JsonElement qrPayload = qr.GetProperty("payload");
        string encodedQr = PairingQrCodec.Encode(new(
            qrPayload.GetProperty("v").GetInt32(),
            qrPayload.GetProperty("agentId").GetString()!,
            new Uri(qrPayload.GetProperty("endpoint").GetString()!),
            qrPayload.GetProperty("spkiSha256").GetString()!,
            qrPayload.GetProperty("pairingToken").GetString()!,
            qrPayload.GetProperty("expiresAtUnixMs").GetInt64()));
        Assert.Equal(qr.GetProperty("json").GetString(), encodedQr);

        JsonElement auth = fixture.RootElement.GetProperty("authRequest");
        string signature = AuthSignature.Create(
            Convert.FromHexString(auth.GetProperty("secretHex").GetString()!),
            auth.GetProperty("method").GetString()!,
            auth.GetProperty("path").GetString()!,
            auth.GetProperty("agentId").GetString()!,
            auth.GetProperty("deviceId").GetString()!,
            auth.GetProperty("timestampUnixMs").GetInt64(),
            auth.GetProperty("nonce").GetString()!);
        Assert.Equal(auth.GetProperty("signature").GetString(), signature);
    }

    [Fact]
    public void CertificateFingerprintHashesTheSubjectPublicKeyInfo()
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Harmony PC Touchpad Test",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));

        string fingerprint = CertificateFingerprint.ComputeSpkiSha256(certificate);

        Assert.True(Base64Url.TryDecode(fingerprint, out byte[] decoded));
        Assert.Equal(
            SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()),
            decoded);
    }

    private static JsonDocument LoadFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "security-auth.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
