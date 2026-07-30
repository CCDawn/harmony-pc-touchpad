using System.Buffers.Binary;

namespace HarmonyPcTouchpad.Agent.Protocol;

public static class InputFrameDecoder
{
    private const int HeaderBytes = 16;
    private const byte SupportedMajorVersion = 1;
    private const InputFrameFlags KnownFlags =
        InputFrameFlags.Coalescible | InputFrameFlags.Final;

    public static InputFrame Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderBytes)
        {
            throw Violation($"Frame is shorter than the {HeaderBytes}-byte header.");
        }

        byte version = bytes[0];
        if (version != SupportedMajorVersion)
        {
            throw Violation($"Unsupported protocol major version: {version}.");
        }

        InputFrameType type = ReadEnum<InputFrameType>(bytes[1], "frame type");
        InputFrameFlags flags = (InputFrameFlags)BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..4]);
        if ((flags & ~KnownFlags) != 0)
        {
            throw Violation($"Unknown frame flags: 0x{(ushort)flags:X4}.");
        }

        uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]);
        ulong timestampUs = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..16]);
        ReadOnlySpan<byte> payload = bytes[HeaderBytes..];

        return type switch
        {
            InputFrameType.PointerDelta =>
                DecodePointer(version, flags, sequence, timestampUs, payload),
            InputFrameType.Button =>
                DecodeButton(version, flags, sequence, timestampUs, payload),
            InputFrameType.Scroll =>
                DecodeScroll(version, flags, sequence, timestampUs, payload),
            InputFrameType.Gesture =>
                DecodeGesture(version, flags, sequence, timestampUs, payload),
            InputFrameType.ReleaseAll =>
                DecodeReleaseAll(version, flags, sequence, timestampUs, payload),
            _ => throw Violation($"Unknown frame type: {(byte)type}.")
        };
    }

    private static PointerDeltaFrame DecodePointer(
        byte version,
        InputFrameFlags flags,
        uint sequence,
        ulong timestampUs,
        ReadOnlySpan<byte> payload)
    {
        RequirePayload(payload, 12, InputFrameType.PointerDelta);
        RequireExactFlags(flags, InputFrameFlags.Coalescible, InputFrameType.PointerDelta);

        float dx = ReadFiniteSingle(payload, 0, "pointer dx");
        float dy = ReadFiniteSingle(payload, 4, "pointer dy");
        float velocity = ReadFiniteSingle(payload, 8, "pointer velocity");
        if (velocity < 0)
        {
            throw Violation("Pointer velocity must be non-negative.");
        }

        return new(version, flags, sequence, timestampUs, dx, dy, velocity);
    }

    private static ButtonFrame DecodeButton(
        byte version,
        InputFrameFlags flags,
        uint sequence,
        ulong timestampUs,
        ReadOnlySpan<byte> payload)
    {
        RequirePayload(payload, 4, InputFrameType.Button);
        RequireExactFlags(flags, InputFrameFlags.None, InputFrameType.Button);
        RequireZeroReserved(payload[2..], InputFrameType.Button);

        InputButton button = ReadEnum<InputButton>(payload[0], "button");
        ButtonAction action = ReadEnum<ButtonAction>(payload[1], "button action");
        return new(version, flags, sequence, timestampUs, button, action);
    }

    private static ScrollFrame DecodeScroll(
        byte version,
        InputFrameFlags flags,
        uint sequence,
        ulong timestampUs,
        ReadOnlySpan<byte> payload)
    {
        RequirePayload(payload, 12, InputFrameType.Scroll);
        float dx = ReadFiniteSingle(payload, 0, "scroll dx");
        float dy = ReadFiniteSingle(payload, 4, "scroll dy");
        InputPhase phase = ReadEnum<InputPhase>(payload[8], "scroll phase");
        RequireZeroReserved(payload[9..], InputFrameType.Scroll);
        RequirePhaseFlags(flags, phase, InputFrameType.Scroll);
        return new(version, flags, sequence, timestampUs, dx, dy, phase);
    }

    private static GestureFrame DecodeGesture(
        byte version,
        InputFrameFlags flags,
        uint sequence,
        ulong timestampUs,
        ReadOnlySpan<byte> payload)
    {
        RequirePayload(payload, 12, InputFrameType.Gesture);
        GestureKind gesture = ReadEnum<GestureKind>(payload[0], "gesture kind");
        InputPhase phase = ReadEnum<InputPhase>(payload[1], "gesture phase");
        GestureDirection direction = ReadEnum<GestureDirection>(payload[2], "gesture direction");
        if (payload[3] != 0)
        {
            throw Violation("Gesture reserved byte must be zero.");
        }

        float value1 = ReadFiniteSingle(payload, 4, "gesture value1");
        float value2 = ReadFiniteSingle(payload, 8, "gesture value2");

        if (gesture is GestureKind.Pinch or GestureKind.Rotate)
        {
            RequirePhaseFlags(flags, phase, InputFrameType.Gesture);
            if (direction != GestureDirection.None)
            {
                throw Violation($"{gesture} requires direction None.");
            }

            if (gesture == GestureKind.Pinch && value1 <= 0)
            {
                throw Violation("Pinch scale ratio must be greater than zero.");
            }
        }
        else
        {
            RequireExactFlags(flags, InputFrameFlags.Final, InputFrameType.Gesture);
            if (phase != InputPhase.End)
            {
                throw Violation($"{gesture} requires phase End.");
            }

            if (direction == GestureDirection.None)
            {
                throw Violation($"{gesture} requires a direction.");
            }

            if (value1 < 0 || value2 < 0)
            {
                throw Violation($"{gesture} distance and speed must be non-negative.");
            }
        }

        return new(
            version,
            flags,
            sequence,
            timestampUs,
            gesture,
            phase,
            direction,
            value1,
            value2);
    }

    private static ReleaseAllFrame DecodeReleaseAll(
        byte version,
        InputFrameFlags flags,
        uint sequence,
        ulong timestampUs,
        ReadOnlySpan<byte> payload)
    {
        RequirePayload(payload, 0, InputFrameType.ReleaseAll);
        RequireExactFlags(flags, InputFrameFlags.Final, InputFrameType.ReleaseAll);
        return new(version, flags, sequence, timestampUs);
    }

    private static void RequirePhaseFlags(
        InputFrameFlags flags,
        InputPhase phase,
        InputFrameType type)
    {
        InputFrameFlags expected = phase switch
        {
            InputPhase.Begin => InputFrameFlags.None,
            InputPhase.Update => InputFrameFlags.Coalescible,
            InputPhase.End or InputPhase.Cancel => InputFrameFlags.Final,
            _ => throw Violation($"Unknown phase: {(byte)phase}.")
        };

        RequireExactFlags(flags, expected, type);
    }

    private static void RequireExactFlags(
        InputFrameFlags actual,
        InputFrameFlags expected,
        InputFrameType type)
    {
        if (actual != expected)
        {
            throw Violation(
                $"{type} requires exactly the {FormatFlags(expected)} flag set; received {FormatFlags(actual)}.");
        }
    }

    private static string FormatFlags(InputFrameFlags flags) =>
        flags == InputFrameFlags.None ? "None" : flags.ToString();

    private static void RequirePayload(
        ReadOnlySpan<byte> payload,
        int expectedBytes,
        InputFrameType type)
    {
        if (payload.Length != expectedBytes)
        {
            throw Violation(
                $"{type} payload must be exactly {expectedBytes} bytes; received {payload.Length}.");
        }
    }

    private static void RequireZeroReserved(ReadOnlySpan<byte> bytes, InputFrameType type)
    {
        foreach (byte value in bytes)
        {
            if (value != 0)
            {
                throw Violation($"{type} reserved bytes must be zero.");
            }
        }
    }

    private static float ReadFiniteSingle(ReadOnlySpan<byte> bytes, int offset, string field)
    {
        float value = BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, sizeof(float))));
        if (!float.IsFinite(value))
        {
            throw Violation($"{field} must be finite.");
        }

        return value;
    }

    private static T ReadEnum<T>(byte value, string field)
        where T : struct, Enum
    {
        T result = (T)Enum.ToObject(typeof(T), value);
        if (!Enum.IsDefined(result))
        {
            throw Violation($"Unknown {field}: {value}.");
        }

        return result;
    }

    private static ProtocolViolationException Violation(string message) => new(message);
}
