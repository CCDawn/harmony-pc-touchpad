using HarmonyPcTouchpad.Agent.Security;

namespace HarmonyPcTouchpad.Agent.Windows.Tests;

public sealed class WindowsAgentIdentityStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"hpt-identity-tests-{Guid.NewGuid():N}");

    [Fact]
    public void IdentityAndCertificateRemainStableAcrossRestarts()
    {
        string path = Path.Combine(_directory, "identity.json");
        var protector = new RecordingProtector();
        var store = new WindowsAgentIdentityStore(path, protector);

        using WindowsAgentIdentity first = store.LoadOrCreate();
        using WindowsAgentIdentity second =
            new WindowsAgentIdentityStore(path, protector).LoadOrCreate();

        Assert.Equal(first.AgentId, second.AgentId);
        Assert.Equal($"{first.AgentId}.local", first.HostName);
        Assert.Equal(
            CertificateFingerprint.ComputeSpkiSha256(first.Certificate),
            CertificateFingerprint.ComputeSpkiSha256(second.Certificate));
        Assert.True(first.Certificate.HasPrivateKey);
        Assert.True(protector.ProtectCalls > 0);
        Assert.True(protector.UnprotectCalls > 0);
    }

    [Fact]
    public void CorruptIdentityFailsClosedInsteadOfRotatingTheCertificate()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "identity.json");
        File.WriteAllText(path, """{"schemaVersion":1,"agentId":"agent-valid"}""");

        Assert.Throws<InvalidDataException>(
            () => new WindowsAgentIdentityStore(path, new RecordingProtector())
                .LoadOrCreate());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class RecordingProtector : ISecretProtector
    {
        public int ProtectCalls { get; private set; }

        public int UnprotectCalls { get; private set; }

        public byte[] Protect(ReadOnlySpan<byte> plaintext)
        {
            ProtectCalls++;
            return plaintext.ToArray();
        }

        public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
        {
            UnprotectCalls++;
            return protectedData.ToArray();
        }
    }
}
