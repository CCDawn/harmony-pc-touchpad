using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HarmonyPcTouchpad.Agent.Security;

public interface IDeviceCredentialWriter
{
    void SaveSecret(string deviceId, ReadOnlySpan<byte> deviceSecret);

    bool DeleteSecret(string deviceId);
}

public sealed class PairingRejectedException : Exception
{
    public PairingRejectedException(string message)
        : base(message)
    {
    }
}

public sealed record IssuedDeviceCredential(string DeviceId, string DeviceSecret);

public sealed class PairingAuthority
{
    public const int DeviceSecretBytes = 32;

    private readonly PairingTicketService _tickets;
    private readonly IDeviceCredentialWriter _credentials;
    private readonly ISecureRandom _random;

    public PairingAuthority(
        PairingTicketService tickets,
        IDeviceCredentialWriter credentials,
        ISecureRandom random)
    {
        _tickets = tickets ?? throw new ArgumentNullException(nameof(tickets));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public IssuedDeviceCredential Complete(string pairingToken, string deviceId)
    {
        if (!SecurityIdentifiers.IsValid(deviceId))
        {
            throw new PairingRejectedException("The device identity is invalid.");
        }

        if (!_tickets.TryConsume(pairingToken))
        {
            throw new PairingRejectedException("The pairing ticket is invalid or expired.");
        }

        Span<byte> deviceSecret = stackalloc byte[DeviceSecretBytes];
        _random.Fill(deviceSecret);
        try
        {
            _credentials.SaveSecret(deviceId, deviceSecret);
            return new(deviceId, Base64Url.Encode(deviceSecret));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(deviceSecret);
        }
    }
}

public sealed record PairingQrPayload(
    int Version,
    string AgentId,
    Uri Endpoint,
    string SpkiSha256,
    string PairingToken,
    long ExpiresAtUnixMs);

public static class PairingQrCodec
{
    public static string Encode(PairingQrPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Validate(payload);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", payload.Version);
            writer.WriteString("agentId", payload.AgentId);
            writer.WriteString("endpoint", payload.Endpoint.AbsoluteUri);
            writer.WriteString("spkiSha256", payload.SpkiSha256);
            writer.WriteString("pairingToken", payload.PairingToken);
            writer.WriteNumber("expiresAtUnixMs", payload.ExpiresAtUnixMs);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Validate(PairingQrPayload payload)
    {
        if (payload.Version != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), "QR version must be 1.");
        }

        if (!SecurityIdentifiers.IsValid(payload.AgentId))
        {
            throw new ArgumentException("Agent ID is invalid.", nameof(payload));
        }

        if (!payload.Endpoint.IsAbsoluteUri ||
            payload.Endpoint.Scheme != Uri.UriSchemeWss ||
            payload.Endpoint.AbsolutePath != "/pair" ||
            !string.IsNullOrEmpty(payload.Endpoint.Query) ||
            !string.IsNullOrEmpty(payload.Endpoint.Fragment) ||
            !string.IsNullOrEmpty(payload.Endpoint.UserInfo))
        {
            throw new ArgumentException(
                "Pairing endpoint must be an absolute wss:// URL ending in /pair without query, fragment, or user information.",
                nameof(payload));
        }

        RequireThirtyTwoBytes(payload.SpkiSha256, "SPKI fingerprint");
        RequireThirtyTwoBytes(payload.PairingToken, "pairing token");
        if (payload.ExpiresAtUnixMs <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                "Pairing expiry must be a positive Unix millisecond timestamp.");
        }
    }

    private static void RequireThirtyTwoBytes(string value, string field)
    {
        if (!Base64Url.TryDecode(value, out byte[] decoded))
        {
            throw new ArgumentException($"{field} must be unpadded base64url.");
        }

        try
        {
            if (decoded.Length != 32)
            {
                throw new ArgumentException($"{field} must contain exactly 32 bytes.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
        }
    }
}
