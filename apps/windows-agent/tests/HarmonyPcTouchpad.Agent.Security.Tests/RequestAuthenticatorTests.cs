namespace HarmonyPcTouchpad.Agent.Security.Tests;

public sealed class RequestAuthenticatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidSignatureAuthenticatesOnceAndReplayIsRejected()
    {
        byte[] secret = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var store = new RecordingCredentialStore("phone-001", secret);
        var authenticator = CreateAuthenticator(store);
        AuthRequest request = Sign(secret, nonceByte: 0x33);

        Assert.True(authenticator.TryAuthenticate(request));
        Assert.False(authenticator.TryAuthenticate(request));
    }

    [Fact]
    public void InvalidSignatureDoesNotBurnTheNonce()
    {
        byte[] secret = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var store = new RecordingCredentialStore("phone-001", secret);
        var authenticator = CreateAuthenticator(store);
        AuthRequest valid = Sign(secret, nonceByte: 0x44);
        AuthRequest invalid = valid with { Signature = Base64Url.Encode(new byte[32]) };

        Assert.False(authenticator.TryAuthenticate(invalid));
        Assert.True(authenticator.TryAuthenticate(valid));
    }

    [Fact]
    public void StaleTimestampAndTamperedPathAreRejected()
    {
        byte[] secret = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var store = new RecordingCredentialStore("phone-001", secret);
        var authenticator = CreateAuthenticator(store);
        AuthRequest valid = Sign(secret, nonceByte: 0x55);

        Assert.False(authenticator.TryAuthenticate(
            valid with { TimestampUnixMs = Now.AddMinutes(-1).ToUnixTimeMilliseconds() }));
        Assert.False(authenticator.TryAuthenticate(valid with { Path = "/admin" }));
    }

    [Fact]
    public void UnknownDeviceIsRejectedWithoutExposingASecret()
    {
        var store = new RecordingCredentialStore(
            "another-phone",
            Enumerable.Repeat((byte)1, 32).ToArray());
        var authenticator = CreateAuthenticator(store);
        AuthRequest request = Sign(new byte[32], nonceByte: 0x66);

        Assert.False(authenticator.TryAuthenticate(request));
    }

    [Fact]
    public async Task ConcurrentReplayAllowsExactlyOneAuthentication()
    {
        byte[] secret = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var store = new RecordingCredentialStore("phone-001", secret);
        var authenticator = CreateAuthenticator(store);
        AuthRequest request = Sign(secret, nonceByte: 0x77);

        bool[] results = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => authenticator.TryAuthenticate(request))));

        Assert.Single(results, value => value);
    }

    [Fact]
    public void ReplayEntryIsRetainedForTwoMinutesFromAcceptance()
    {
        byte[] secret = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var store = new RecordingCredentialStore("phone-001", secret);
        var clock = new AdjustableTimeProvider(Now);
        var authenticator = CreateAuthenticator(store, clock);
        AuthRequest first = Sign(
            secret,
            nonceByte: 0x78,
            Now.AddSeconds(-29).ToUnixTimeMilliseconds());

        Assert.True(authenticator.TryAuthenticate(first));

        clock.Advance(TimeSpan.FromSeconds(119));
        AuthRequest replay = Sign(
            secret,
            nonceByte: 0x78,
            clock.GetUtcNow().ToUnixTimeMilliseconds());

        Assert.False(authenticator.TryAuthenticate(replay));
    }

    [Theory]
    [InlineData(29, 120)]
    [InlineData(31, 120)]
    [InlineData(30, 119)]
    [InlineData(30, 121)]
    public void AuthenticatorRejectsTimingPoliciesOutsideTheFrozenContract(
        int clockSkewSeconds,
        int replayLifetimeSeconds)
    {
        var store = new RecordingCredentialStore("phone-001", new byte[32]);

        Assert.Throws<ArgumentOutOfRangeException>(() => new RequestAuthenticator(
            "agent-001",
            store,
            new FixedTimeProvider(Now),
            TimeSpan.FromSeconds(clockSkewSeconds),
            TimeSpan.FromSeconds(replayLifetimeSeconds)));
    }

    [Fact]
    public void SignerRejectsMaterialOutsideTheFrozenContract()
    {
        string nonce = Base64Url.Encode(new byte[16]);

        Assert.Throws<ArgumentException>(() => AuthSignature.Create(
            new byte[31],
            "GET",
            "/input",
            "agent-001",
            "phone-001",
            Now.ToUnixTimeMilliseconds(),
            nonce));
        Assert.Throws<ArgumentException>(() => AuthSignature.Create(
            new byte[32],
            "GET",
            "/admin",
            "agent-001",
            "phone-001",
            Now.ToUnixTimeMilliseconds(),
            nonce));
    }

    private static RequestAuthenticator CreateAuthenticator(
        IDeviceCredentialStore store,
        TimeProvider? clock = null) =>
        new(
            "agent-001",
            store,
            clock ?? new FixedTimeProvider(Now),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2));

    private static AuthRequest Sign(
        byte[] secret,
        byte nonceByte,
        long? timestampUnixMs = null)
    {
        string nonce = Base64Url.Encode(Enumerable.Repeat(nonceByte, 16).ToArray());
        long timestamp = timestampUnixMs ?? Now.ToUnixTimeMilliseconds();
        string signature = AuthSignature.Create(
            secret,
            "GET",
            "/input",
            "agent-001",
            "phone-001",
            timestamp,
            nonce);
        return new(
            "GET",
            "/input",
            "phone-001",
            timestamp,
            nonce,
            signature);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount) => _now = _now.Add(amount);
    }

    private sealed class RecordingCredentialStore(
        string expectedDeviceId,
        byte[] secret) : IDeviceCredentialStore
    {
        public bool TryGetSecret(string deviceId, out byte[] deviceSecret)
        {
            if (deviceId != expectedDeviceId)
            {
                deviceSecret = [];
                return false;
            }

            deviceSecret = secret.ToArray();
            return true;
        }
    }
}
