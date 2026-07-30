namespace HarmonyPcTouchpad.Agent.Transport.Tests;

public sealed class InputRateLimiterTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MoreThanTheFrozenInputRateIsRejectedWithinOneSecond()
    {
        var clock = new AdjustableTimeProvider(Now);
        var limiter = new InputRateLimiter(clock);

        for (int index = 0; index < TransportPolicy.MaxInputRateHz; index++)
        {
            Assert.True(limiter.TryAccept());
        }

        Assert.False(limiter.TryAccept());

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(limiter.TryAccept());
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        private long _timestamp;

        public override DateTimeOffset GetUtcNow() => _now;

        public override long GetTimestamp() => _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan amount)
        {
            _now = _now.Add(amount);
            _timestamp += amount.Ticks;
        }
    }
}
