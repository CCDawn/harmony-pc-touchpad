using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HarmonyPcTouchpad.Agent.Security;

public interface IDeviceCredentialStore
{
    bool TryGetSecret(string deviceId, out byte[] deviceSecret);
}

public sealed record AuthRequest(
    string Method,
    string Path,
    string DeviceId,
    long TimestampUnixMs,
    string Nonce,
    string Signature);

public static class AuthSignature
{
    public static string Create(
        ReadOnlySpan<byte> secret,
        string method,
        string path,
        string agentId,
        string deviceId,
        long timestampUnixMs,
        string nonce)
    {
        ValidateSigningMaterial(
            secret,
            method,
            path,
            agentId,
            deviceId,
            timestampUnixMs,
            nonce);
        byte[] signature = Compute(
            secret,
            method,
            path,
            agentId,
            deviceId,
            timestampUnixMs,
            nonce);
        try
        {
            return Base64Url.Encode(signature);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    internal static byte[] Compute(
        ReadOnlySpan<byte> secret,
        string method,
        string path,
        string agentId,
        string deviceId,
        long timestampUnixMs,
        string nonce)
    {
        string canonical = string.Join(
            '\n',
            "HPT1",
            method,
            path,
            agentId,
            deviceId,
            timestampUnixMs.ToString(CultureInfo.InvariantCulture),
            nonce);
        byte[] canonicalBytes = Encoding.UTF8.GetBytes(canonical);
        try
        {
            return HMACSHA256.HashData(secret, canonicalBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonicalBytes);
        }
    }

    private static void ValidateSigningMaterial(
        ReadOnlySpan<byte> secret,
        string method,
        string path,
        string agentId,
        string deviceId,
        long timestampUnixMs,
        string nonce)
    {
        if (secret.Length != 32)
        {
            throw new ArgumentException("Device secret must contain exactly 32 bytes.");
        }

        if (method != "GET" || path != RequestAuthenticator.InputPath)
        {
            throw new ArgumentException("Authenticated request must be GET /input.");
        }

        if (!SecurityIdentifiers.IsValid(agentId) ||
            !SecurityIdentifiers.IsValid(deviceId))
        {
            throw new ArgumentException("Agent ID or device ID is invalid.");
        }

        if (timestampUnixMs <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestampUnixMs),
                "Authentication timestamp must be positive.");
        }

        if (!Base64Url.TryDecode(nonce, out byte[] decodedNonce))
        {
            throw new ArgumentException("Nonce must be unpadded base64url.", nameof(nonce));
        }

        try
        {
            if (decodedNonce.Length != 16)
            {
                throw new ArgumentException(
                    "Nonce must contain exactly 16 bytes.",
                    nameof(nonce));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decodedNonce);
        }
    }
}

public sealed class RequestAuthenticator
{
    public const string InputPath = "/input";

    private const int SecretBytes = 32;
    private const int NonceBytes = 16;
    private const int SignatureBytes = 32;
    private const int MaxReplayEntries = 4096;

    private readonly object _replayGate = new();
    private readonly string _agentId;
    private readonly IDeviceCredentialStore _credentials;
    private readonly TimeProvider _clock;
    private readonly long _allowedClockSkewMs;
    private readonly long _replayLifetimeMs;
    private readonly Dictionary<string, long> _usedNonces = [];

    public RequestAuthenticator(
        string agentId,
        IDeviceCredentialStore credentials,
        TimeProvider clock,
        TimeSpan allowedClockSkew,
        TimeSpan replayLifetime)
    {
        if (!SecurityIdentifiers.IsValid(agentId))
        {
            throw new ArgumentException("Agent ID is invalid.", nameof(agentId));
        }

        if (allowedClockSkew <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(allowedClockSkew));
        }

        if (replayLifetime < allowedClockSkew)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replayLifetime),
                "Replay lifetime must be at least the allowed clock skew.");
        }

        _agentId = agentId;
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _allowedClockSkewMs = checked((long)allowedClockSkew.TotalMilliseconds);
        _replayLifetimeMs = checked((long)replayLifetime.TotalMilliseconds);
    }

    public bool TryAuthenticate(AuthRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Method != "GET" ||
            request.Path != InputPath ||
            !SecurityIdentifiers.IsValid(request.DeviceId) ||
            !IsCurrent(request.TimestampUnixMs))
        {
            return false;
        }

        if (!Base64Url.TryDecode(request.Nonce, out byte[] nonce))
        {
            return false;
        }

        try
        {
            if (nonce.Length != NonceBytes ||
                !Base64Url.TryDecode(request.Signature, out byte[] suppliedSignature))
            {
                return false;
            }

            try
            {
                if (suppliedSignature.Length != SignatureBytes ||
                    !_credentials.TryGetSecret(request.DeviceId, out byte[] secret))
                {
                    return false;
                }

                try
                {
                    if (secret.Length != SecretBytes)
                    {
                        return false;
                    }

                    byte[] expectedSignature = AuthSignature.Compute(
                        secret,
                        request.Method,
                        request.Path,
                        _agentId,
                        request.DeviceId,
                        request.TimestampUnixMs,
                        request.Nonce);
                    try
                    {
                        if (!CryptographicOperations.FixedTimeEquals(
                                suppliedSignature,
                                expectedSignature))
                        {
                            return false;
                        }
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(expectedSignature);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(secret);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(suppliedSignature);
            }

            return TryRegisterNonce(
                request.DeviceId,
                request.Nonce,
                request.TimestampUnixMs);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    private bool IsCurrent(long timestampUnixMs)
    {
        long now = _clock.GetUtcNow().ToUnixTimeMilliseconds();
        return timestampUnixMs >= now - _allowedClockSkewMs &&
            timestampUnixMs <= now + _allowedClockSkewMs;
    }

    private bool TryRegisterNonce(string deviceId, string nonce, long timestampUnixMs)
    {
        long now = _clock.GetUtcNow().ToUnixTimeMilliseconds();
        string key = string.Concat(deviceId, "\n", nonce);

        lock (_replayGate)
        {
            foreach (string expired in _usedNonces
                         .Where(entry => entry.Value <= now)
                         .Select(entry => entry.Key)
                         .ToArray())
            {
                _usedNonces.Remove(expired);
            }

            if (_usedNonces.ContainsKey(key) ||
                _usedNonces.Count >= MaxReplayEntries)
            {
                return false;
            }

            long timestampExpiry = timestampUnixMs + _replayLifetimeMs;
            _usedNonces.Add(key, Math.Max(now + _allowedClockSkewMs, timestampExpiry));
            return true;
        }
    }

}

public static class SecurityIdentifiers
{
    public static bool IsValid(string value) =>
        !string.IsNullOrEmpty(value) &&
        value.Length <= 128 &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or '.');
}
