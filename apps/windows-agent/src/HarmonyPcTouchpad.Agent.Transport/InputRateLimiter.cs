namespace HarmonyPcTouchpad.Agent.Transport;

public sealed class InputRateLimiter
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly int _maxRateHz;
    private readonly Queue<long> _acceptedTimestamps = [];

    public InputRateLimiter(TimeProvider clock)
        : this(clock, TransportPolicy.MaxInputRateHz)
    {
    }

    internal InputRateLimiter(TimeProvider clock, int maxRateHz)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (maxRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRateHz));
        }

        _maxRateHz = maxRateHz;
    }

    public bool TryAccept()
    {
        long now = _clock.GetTimestamp();
        lock (_gate)
        {
            while (_acceptedTimestamps.TryPeek(out long acceptedAt) &&
                   _clock.GetElapsedTime(acceptedAt, now) >= TimeSpan.FromSeconds(1))
            {
                _acceptedTimestamps.Dequeue();
            }

            if (_acceptedTimestamps.Count >= _maxRateHz)
            {
                return false;
            }

            _acceptedTimestamps.Enqueue(now);
            return true;
        }
    }
}
