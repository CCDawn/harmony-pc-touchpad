namespace HarmonyPcTouchpad.Agent.Security;

public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static bool TryDecode(string value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrEmpty(value) ||
            value.Contains('=') ||
            value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_'))
        {
            return false;
        }

        string base64 = value.Replace('-', '+').Replace('_', '/');
        int padding = (4 - (base64.Length % 4)) % 4;
        base64 = base64.PadRight(base64.Length + padding, '=');

        try
        {
            bytes = Convert.FromBase64String(base64);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
