namespace HarmonyPcTouchpad.Agent.Security.Tests;

public sealed class PairingAuthorityTests
{
    [Fact]
    public void PairingQrIsDeterministicAndContainsNoNetworkQueryToken()
    {
        var ticket = new PairingTicket(
            Base64Url.Encode(Enumerable.Repeat((byte)0x5A, 32).ToArray()),
            DateTimeOffset.FromUnixTimeMilliseconds(1_775_000_000_000));
        string fingerprint =
            Base64Url.Encode(Enumerable.Repeat((byte)0xA5, 32).ToArray());

        string json = PairingQrCodec.Encode(new(
            1,
            "agent-001",
            new Uri("wss://touchpad.local:47431/pair"),
            fingerprint,
            ticket.Token,
            ticket.ExpiresAt.ToUnixTimeMilliseconds()));

        Assert.Equal(
            """{"v":1,"agentId":"agent-001","endpoint":"wss://touchpad.local:47431/pair","spkiSha256":"paWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaU","pairingToken":"WlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlpaWlo","expiresAtUnixMs":1775000000000}""",
            json);
        Assert.DoesNotContain("?token=", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidTicketIssuesAndStoresADeviceSecretOnce()
    {
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero));
        var random = new SequenceRandomSource(0x10, 0x20);
        var tickets = new PairingTicketService(clock, random);
        var store = new RecordingCredentialStore();
        var authority = new PairingAuthority(tickets, store, random);
        PairingTicket ticket = tickets.Issue();

        IssuedDeviceCredential issued = authority.Complete(ticket.Token, "phone-001");

        Assert.Equal("phone-001", issued.DeviceId);
        Assert.Equal(43, issued.DeviceSecret.Length);
        Assert.True(store.TryGetSecret("phone-001", out byte[] stored));
        Assert.True(Base64Url.TryDecode(issued.DeviceSecret, out byte[] delivered));
        Assert.Equal(stored, delivered);
        Assert.All(stored, value => Assert.Equal((byte)0x20, value));
        Assert.Throws<PairingRejectedException>(
            () => authority.Complete(ticket.Token, "phone-001"));
    }

    [Fact]
    public async Task ConcurrentConsumersCannotReuseAPairingTicket()
    {
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero));
        var tickets = new PairingTicketService(
            clock,
            new SequenceRandomSource(0x30));
        PairingTicket ticket = tickets.Issue();

        bool[] results = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => tickets.TryConsume(ticket.Token))));

        Assert.Single(results, value => value);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SequenceRandomSource(params byte[] values) : ISecureRandom
    {
        private int _index;

        public void Fill(Span<byte> destination)
        {
            destination.Fill(values[_index]);
            _index++;
        }
    }

    private sealed class RecordingCredentialStore :
        IDeviceCredentialStore,
        IDeviceCredentialWriter
    {
        private readonly Dictionary<string, byte[]> _secrets = [];

        public void SaveSecret(string deviceId, ReadOnlySpan<byte> deviceSecret) =>
            _secrets[deviceId] = deviceSecret.ToArray();

        public bool DeleteSecret(string deviceId) => _secrets.Remove(deviceId);

        public bool TryGetSecret(string deviceId, out byte[] deviceSecret)
        {
            if (_secrets.TryGetValue(deviceId, out byte[]? secret))
            {
                deviceSecret = secret.ToArray();
                return true;
            }

            deviceSecret = [];
            return false;
        }
    }
}
