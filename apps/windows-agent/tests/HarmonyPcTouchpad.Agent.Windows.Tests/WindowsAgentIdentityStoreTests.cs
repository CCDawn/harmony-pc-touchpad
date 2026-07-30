using HarmonyPcTouchpad.Agent.Security;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

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
    public void IdentityCertificateIncludesRequestedPrivateAddress()
    {
        string path = Path.Combine(_directory, "identity.json");
        IPAddress address = IPAddress.Parse("192.168.20.18");

        using WindowsAgentIdentity identity =
            new WindowsAgentIdentityStore(path, new RecordingProtector())
                .LoadOrCreate([address]);

        X509Extension extension =
            Assert.Single(
                identity.Certificate.Extensions
                    .Cast<X509Extension>(),
                candidate => candidate.Oid?.Value == "2.5.29.17");
        var subjectNames = new X509SubjectAlternativeNameExtension(
            extension.RawData,
            extension.Critical);
        Assert.Contains(address, subjectNames.EnumerateIPAddresses());
    }

    [Fact]
    public void MissingAddressRotatesCertificateButPreservesAgentIdentity()
    {
        string path = Path.Combine(_directory, "identity.json");
        var protector = new RecordingProtector();
        var store = new WindowsAgentIdentityStore(path, protector);
        using WindowsAgentIdentity original = store.LoadOrCreate();
        string originalFingerprint =
            CertificateFingerprint.ComputeSpkiSha256(original.Certificate);

        IPAddress address = IPAddress.Parse("192.168.20.18");
        using WindowsAgentIdentity rotated = store.LoadOrCreate([address]);
        using WindowsAgentIdentity stable = store.LoadOrCreate([address]);

        Assert.Equal(original.AgentId, rotated.AgentId);
        Assert.NotEqual(
            originalFingerprint,
            CertificateFingerprint.ComputeSpkiSha256(rotated.Certificate));
        Assert.Equal(
            CertificateFingerprint.ComputeSpkiSha256(rotated.Certificate),
            CertificateFingerprint.ComputeSpkiSha256(stable.Certificate));
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

    [Fact]
    public async Task StoredIdentityCompletesAWindowsSchannelServerHandshake()
    {
        string path = Path.Combine(_directory, "identity.json");
        using WindowsAgentIdentity identity =
            new WindowsAgentIdentityStore(path, new RecordingProtector())
                .LoadOrCreate();
        string expectedFingerprint =
            CertificateFingerprint.ComputeSpkiSha256(identity.Certificate);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task server = Task.Run(async () =>
            {
                using TcpClient accepted = await listener.AcceptTcpClientAsync();
                using var serverTls = new SslStream(accepted.GetStream());
                await serverTls.AuthenticateAsServerAsync(identity.Certificate);
            });

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            using var clientTls = new SslStream(
                client.GetStream(),
                leaveInnerStreamOpen: false,
                (_, certificate, _, _) =>
                    certificate is X509Certificate2 actual &&
                    CertificateFingerprint.ComputeSpkiSha256(actual) ==
                        expectedFingerprint);
            await clientTls.AuthenticateAsClientAsync(identity.HostName);
            await server;
        }
        finally
        {
            listener.Stop();
        }
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
