using System.Globalization;
using System.Text.Json;
using HarmonyPcTouchpad.Agent.Protocol;
using HarmonyPcTouchpad.Agent.Security;

namespace HarmonyPcTouchpad.Agent.Transport;

internal static class ControlMessageCodec
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 8
    };

    public static string ReadHello(string json)
    {
        using JsonDocument document = Parse(json);
        JsonElement root = document.RootElement;
        RequireEnvelope(root, "HELLO", sessionRequired: false);
        JsonElement payload = RequireObject(root, "payload");
        string deviceId = RequireString(payload, "deviceId", 128);
        if (!SecurityIdentifiers.IsValid(deviceId))
        {
            throw Violation("HELLO device ID is invalid.");
        }

        _ = RequireString(payload, "deviceName", 128);
        JsonElement capabilities = RequireArray(payload, "capabilities");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement capability in capabilities.EnumerateArray())
        {
            if (capability.ValueKind != JsonValueKind.String ||
                string.IsNullOrEmpty(capability.GetString()) ||
                !seen.Add(capability.GetString()!))
            {
                throw Violation("HELLO capabilities are invalid.");
            }
        }

        return deviceId;
    }

    public static void RequireControlRequest(string json, string sessionId)
    {
        using JsonDocument document = Parse(json);
        JsonElement root = document.RootElement;
        RequireEnvelope(root, "CONTROL_REQUEST", sessionRequired: true);
        if (RequireString(root, "sessionId", 128) != sessionId)
        {
            throw Violation("CONTROL_REQUEST session ID does not match.");
        }

        _ = RequireObject(root, "payload");
    }

    public static bool TryReadHeartbeat(
        string json,
        string sessionId,
        out string? pong)
    {
        using JsonDocument document = Parse(json);
        JsonElement root = document.RootElement;
        string kind = RequireString(root, "kind", 32);
        if (kind is not ("PING" or "PONG"))
        {
            pong = null;
            return false;
        }

        RequireEnvelope(root, kind, sessionRequired: true);
        if (RequireString(root, "sessionId", 128) != sessionId)
        {
            throw Violation($"{kind} session ID does not match.");
        }

        string nonce = RequireString(RequireObject(root, "payload"), "nonce", 64);
        pong = kind == "PING" ? nonce : null;
        return true;
    }

    public static string CreateHelloAck(
        string sessionId,
        string messageId,
        string sentAtUs) =>
        JsonSerializer.Serialize(new
        {
            protocol = new { major = 1, minor = 0 },
            kind = "HELLO_ACK",
            messageId,
            sessionId,
            sentAtUs,
            payload = new
            {
                heartbeatMs = (int)TransportPolicy.HeartbeatInterval.TotalMilliseconds,
                idleReleaseMs = (int)TransportPolicy.IdleReleaseTimeout.TotalMilliseconds,
                maxInputRateHz = TransportPolicy.MaxInputRateHz,
                capabilities = new[] { "pointer-delta", "scroll-v1", "gesture-v1" }
            }
        });

    public static string CreateControlGranted(
        string sessionId,
        string deviceId,
        string messageId,
        string sentAtUs) =>
        JsonSerializer.Serialize(new
        {
            protocol = new { major = 1, minor = 0 },
            kind = "CONTROL_GRANTED",
            messageId,
            sessionId,
            sentAtUs,
            payload = new { controllerDeviceId = deviceId }
        });

    public static string CreatePong(
        string sessionId,
        string nonce,
        string messageId,
        string sentAtUs) =>
        JsonSerializer.Serialize(new
        {
            protocol = new { major = 1, minor = 0 },
            kind = "PONG",
            messageId,
            sessionId,
            sentAtUs,
            payload = new { nonce }
        });

    public static string CreatePairingAccepted(
        string deviceId,
        string deviceSecret,
        string messageId,
        string sentAtUs) =>
        JsonSerializer.Serialize(new
        {
            protocol = new { major = 1, minor = 0 },
            kind = "PAIRING_ACCEPTED",
            messageId,
            sessionId = (string?)null,
            sentAtUs,
            payload = new
            {
                deviceId,
                secretVersion = 1,
                deviceSecret
            }
        });

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException error)
        {
            throw Violation("Control message is not valid JSON.", error);
        }
    }

    private static void RequireEnvelope(
        JsonElement root,
        string expectedKind,
        bool sessionRequired)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Violation("Control message must be an object.");
        }

        JsonElement protocol = RequireObject(root, "protocol");
        if (!protocol.TryGetProperty("major", out JsonElement major) ||
            major.ValueKind != JsonValueKind.Number ||
            !major.TryGetInt32(out int majorValue) ||
            majorValue != 1 ||
            !protocol.TryGetProperty("minor", out JsonElement minor) ||
            minor.ValueKind != JsonValueKind.Number ||
            !minor.TryGetUInt16(out _))
        {
            throw Violation("Control protocol version is invalid.");
        }

        if (RequireString(root, "kind", 32) != expectedKind)
        {
            throw Violation($"Expected {expectedKind} control message.");
        }

        _ = RequireString(root, "messageId", 64);
        if (sessionRequired)
        {
            _ = RequireString(root, "sessionId", 128);
        }
        else if (!root.TryGetProperty("sessionId", out JsonElement session) ||
                 session.ValueKind != JsonValueKind.Null)
        {
            throw Violation($"{expectedKind} session ID must be null.");
        }

        string sentAtUs = RequireString(root, "sentAtUs", 32);
        if (!ulong.TryParse(
                sentAtUs,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out _))
        {
            throw Violation("Control timestamp is invalid.");
        }
    }

    private static JsonElement RequireObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            throw Violation($"{name} must be an object.");
        }

        return value;
    }

    private static JsonElement RequireArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            throw Violation($"{name} must be an array.");
        }

        return value;
    }

    private static string RequireString(
        JsonElement parent,
        string name,
        int maxLength)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrEmpty(value.GetString()) ||
            value.GetString()!.Length > maxLength)
        {
            throw Violation($"{name} must be a bounded non-empty string.");
        }

        return value.GetString()!;
    }

    private static ProtocolViolationException Violation(
        string message,
        Exception? inner = null) =>
        inner is null
            ? new(message)
            : new ProtocolViolationException(message, inner);
}
