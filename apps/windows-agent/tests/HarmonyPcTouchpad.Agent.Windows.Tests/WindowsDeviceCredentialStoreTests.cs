using HarmonyPcTouchpad.Agent.Security;

namespace HarmonyPcTouchpad.Agent.Windows.Tests;

public sealed class WindowsDeviceCredentialStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"harmony-touchpad-credentials-{Guid.NewGuid():N}");

    [Fact]
    public void DpapiStoreRoundTripsWithoutWritingPlaintextSecret()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "devices.json");
        var store = new WindowsDeviceCredentialStore(
            path,
            new DpapiSecretProtector());
        byte[] secret = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

        store.SaveSecret("phone-001", secret);

        Assert.True(store.TryGetSecret("phone-001", out byte[] restored));
        Assert.Equal(secret, restored);
        Assert.DoesNotContain(Base64Url.Encode(secret), File.ReadAllText(path));
    }

    [Fact]
    public void CorruptedCredentialFileIsRejectedInsteadOfOverwritten()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "devices.json");
        File.WriteAllText(path, """{"schemaVersion":999,"credentials":[]}""");
        var store = new WindowsDeviceCredentialStore(
            path,
            new DpapiSecretProtector());

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => store.SaveSecret("phone-001", new byte[32]));

        Assert.Contains("schema version", error.Message);
        Assert.Contains("\"schemaVersion\":999", File.ReadAllText(path));
    }

    [Fact]
    public void RevokingADeviceDeletesItsStoredCredential()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "devices.json");
        var store = new WindowsDeviceCredentialStore(
            path,
            new DpapiSecretProtector());
        store.SaveSecret("phone-001", new byte[32]);

        Assert.True(store.DeleteSecret("phone-001"));
        Assert.False(store.TryGetSecret("phone-001", out _));
        Assert.DoesNotContain("phone-001", File.ReadAllText(path));
        Assert.False(store.DeleteSecret("phone-001"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
