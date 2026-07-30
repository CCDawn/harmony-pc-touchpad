using System.Security.Cryptography;

namespace HarmonyPcTouchpad.Agent.Security;

public interface ISecureRandom
{
    void Fill(Span<byte> destination);
}

public sealed class SystemSecureRandom : ISecureRandom
{
    public void Fill(Span<byte> destination) => RandomNumberGenerator.Fill(destination);
}

public sealed record PairingTicket(string Token, DateTimeOffset ExpiresAt);

public sealed class PairingTicketService
{
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(2);

    private const int TokenBytes = 32;
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly ISecureRandom _random;
    private byte[]? _activeTokenHash;
    private DateTimeOffset _expiresAt;

    public PairingTicketService(TimeProvider clock, ISecureRandom random)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public PairingTicket Issue()
    {
        Span<byte> token = stackalloc byte[TokenBytes];
        _random.Fill(token);
        byte[] tokenHash = SHA256.HashData(token);
        string encodedToken = Base64Url.Encode(token);
        CryptographicOperations.ZeroMemory(token);

        lock (_gate)
        {
            ClearActiveHash();
            _activeTokenHash = tokenHash;
            _expiresAt = _clock.GetUtcNow().Add(TicketLifetime);
            return new(encodedToken, _expiresAt);
        }
    }

    public bool TryConsume(string token)
    {
        if (!Base64Url.TryDecode(token, out byte[] candidate) ||
            candidate.Length != TokenBytes)
        {
            CryptographicOperations.ZeroMemory(candidate);
            return false;
        }

        byte[] candidateHash = SHA256.HashData(candidate);
        CryptographicOperations.ZeroMemory(candidate);

        try
        {
            lock (_gate)
            {
                if (_activeTokenHash is null)
                {
                    return false;
                }

                if (_clock.GetUtcNow() >= _expiresAt)
                {
                    ClearActiveHash();
                    return false;
                }

                if (!CryptographicOperations.FixedTimeEquals(
                        candidateHash,
                        _activeTokenHash))
                {
                    return false;
                }

                ClearActiveHash();
                return true;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidateHash);
        }
    }

    private void ClearActiveHash()
    {
        if (_activeTokenHash is not null)
        {
            CryptographicOperations.ZeroMemory(_activeTokenHash);
            _activeTokenHash = null;
        }
    }
}
