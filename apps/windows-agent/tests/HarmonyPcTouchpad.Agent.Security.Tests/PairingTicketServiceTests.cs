namespace HarmonyPcTouchpad.Agent.Security.Tests;

public sealed class PairingTicketServiceTests
{
    [Fact]
    public void TicketIsSingleUseAndOnlyItsHashIsRetained()
    {
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero));
        var service = new PairingTicketService(
            clock,
            new RepeatingRandomSource(0x5A));

        PairingTicket ticket = service.Issue();

        Assert.Equal(43, ticket.Token.Length);
        Assert.Equal(clock.GetUtcNow().AddMinutes(2), ticket.ExpiresAt);
        Assert.True(service.TryConsume(ticket.Token));
        Assert.False(service.TryConsume(ticket.Token));
    }

    [Fact]
    public void WrongOrExpiredTicketCannotBeConsumed()
    {
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero));
        var service = new PairingTicketService(
            clock,
            new RepeatingRandomSource(0x42));
        PairingTicket ticket = service.Issue();

        Assert.False(service.TryConsume(Base64Url.Encode(new byte[32])));

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(service.TryConsume(ticket.Token));
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class RepeatingRandomSource(byte value) : ISecureRandom
    {
        public void Fill(Span<byte> destination) => destination.Fill(value);
    }
}
