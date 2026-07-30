using System.Globalization;
using System.Security.Cryptography;
using HarmonyPcTouchpad.Agent.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace HarmonyPcTouchpad.Agent.Transport;

public sealed record PairingUpgradeHeaders(string DeviceId, string PairingToken);

public static class UpgradeHeaderParser
{
    public static bool TryReadPairing(
        IHeaderDictionary headers,
        out PairingUpgradeHeaders? request)
    {
        ArgumentNullException.ThrowIfNull(headers);
        request = null;
        if (!TrySingle(headers, "X-HPT-Version", out string version) ||
            version != "1" ||
            !TrySingle(headers, "X-HPT-Device-Id", out string deviceId) ||
            !SecurityIdentifiers.IsValid(deviceId) ||
            !TrySingle(headers, "X-HPT-Pairing-Token", out string pairingToken) ||
            !HasExactDecodedLength(pairingToken, 32))
        {
            return false;
        }

        request = new(deviceId, pairingToken);
        return true;
    }

    public static bool TryReadInput(
        IHeaderDictionary headers,
        out AuthRequest? request)
    {
        ArgumentNullException.ThrowIfNull(headers);
        request = null;
        if (!TrySingle(headers, "X-HPT-Version", out string version) ||
            version != "1" ||
            !TrySingle(headers, "X-HPT-Device-Id", out string deviceId) ||
            !SecurityIdentifiers.IsValid(deviceId) ||
            !TrySingle(
                headers,
                "X-HPT-Timestamp-Unix-Ms",
                out string timestampText) ||
            !long.TryParse(
                timestampText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long timestampUnixMs) ||
            timestampUnixMs <= 0 ||
            !TrySingle(headers, "X-HPT-Nonce", out string nonce) ||
            !HasExactDecodedLength(nonce, 16) ||
            !TrySingle(headers, "X-HPT-Signature", out string signature) ||
            !HasExactDecodedLength(signature, 32))
        {
            return false;
        }

        request = new(
            "GET",
            RequestAuthenticator.InputPath,
            deviceId,
            timestampUnixMs,
            nonce,
            signature);
        return true;
    }

    private static bool TrySingle(
        IHeaderDictionary headers,
        string name,
        out string value)
    {
        if (!headers.TryGetValue(name, out StringValues values) ||
            values.Count != 1 ||
            string.IsNullOrEmpty(values[0]))
        {
            value = string.Empty;
            return false;
        }

        value = values[0]!;
        return true;
    }

    private static bool HasExactDecodedLength(string value, int expectedBytes)
    {
        if (!Base64Url.TryDecode(value, out byte[] decoded))
        {
            return false;
        }

        try
        {
            return decoded.Length == expectedBytes;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
        }
    }
}
