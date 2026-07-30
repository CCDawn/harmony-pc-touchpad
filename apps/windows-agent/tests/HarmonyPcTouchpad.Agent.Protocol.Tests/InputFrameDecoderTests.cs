using System.Buffers.Binary;
using System.Text.Json;

namespace HarmonyPcTouchpad.Agent.Protocol.Tests;

public sealed class InputFrameDecoderTests
{
    [Fact]
    public void GoldenVectorsDecodeUsingTheSharedProtocolContract()
    {
        using JsonDocument fixture = LoadFixture();
        JsonElement vectors = fixture.RootElement.GetProperty("vectors");

        Assert.Equal(7, vectors.GetArrayLength());

        foreach (JsonElement vector in vectors.EnumerateArray())
        {
            JsonElement expected = vector.GetProperty("frame");
            byte[] bytes = Convert.FromHexString(vector.GetProperty("hex").GetString()!);

            InputFrame actual = InputFrameDecoder.Decode(bytes);

            Assert.Equal(expected.GetProperty("version").GetByte(), actual.Version);
            Assert.Equal(ReadType(expected.GetProperty("type").GetString()!), actual.Type);
            Assert.Equal(ReadFlags(expected.GetProperty("flags")), actual.Flags);
            Assert.Equal(expected.GetProperty("sequence").GetUInt32(), actual.Sequence);
            Assert.Equal(
                ulong.Parse(expected.GetProperty("timestampUs").GetString()!),
                actual.TimestampUs);
            AssertPayload(expected.GetProperty("payload"), actual);
        }
    }

    [Fact]
    public void UnsupportedVersionIsRejectedBeforePayloadDispatch()
    {
        byte[] frame = new byte[16];
        frame[0] = 2;
        frame[1] = (byte)InputFrameType.ReleaseAll;

        ProtocolViolationException error = Assert.Throws<ProtocolViolationException>(
            () => InputFrameDecoder.Decode(frame));

        Assert.Contains("Unsupported protocol major version", error.Message);
    }

    [Fact]
    public void ReleaseAllWithoutFinalFlagIsRejectedAtTheNetworkBoundary()
    {
        byte[] frame = new byte[16];
        frame[0] = 1;
        frame[1] = (byte)InputFrameType.ReleaseAll;

        ProtocolViolationException error = Assert.Throws<ProtocolViolationException>(
            () => InputFrameDecoder.Decode(frame));

        Assert.Contains("requires exactly the Final flag", error.Message);
    }

    [Fact]
    public void NonFinitePointerValuesAreRejectedAtTheNetworkBoundary()
    {
        byte[] frame = CreateFrame(
            InputFrameType.PointerDelta,
            InputFrameFlags.Coalescible,
            12);
        BinaryPrimitives.WriteInt32LittleEndian(
            frame.AsSpan(16, 4),
            BitConverter.SingleToInt32Bits(0));
        BinaryPrimitives.WriteInt32LittleEndian(
            frame.AsSpan(20, 4),
            BitConverter.SingleToInt32Bits(float.NaN));

        ProtocolViolationException error = Assert.Throws<ProtocolViolationException>(
            () => InputFrameDecoder.Decode(frame));

        Assert.Contains("pointer dy must be finite", error.Message);
    }

    [Fact]
    public void ReservedPayloadBytesMustRemainZero()
    {
        byte[] frame = CreateFrame(InputFrameType.Button, InputFrameFlags.None, 4);
        frame[16] = (byte)InputButton.Left;
        frame[17] = (byte)ButtonAction.Down;
        frame[18] = 1;

        ProtocolViolationException error = Assert.Throws<ProtocolViolationException>(
            () => InputFrameDecoder.Decode(frame));

        Assert.Contains("reserved bytes must be zero", error.Message);
    }

    [Fact]
    public void FinalScrollPhaseCannotBeMarkedCoalescible()
    {
        byte[] frame = CreateFrame(
            InputFrameType.Scroll,
            InputFrameFlags.Coalescible,
            12);
        frame[24] = (byte)InputPhase.End;

        ProtocolViolationException error = Assert.Throws<ProtocolViolationException>(
            () => InputFrameDecoder.Decode(frame));

        Assert.Contains("requires exactly the Final flag", error.Message);
    }

    [Fact]
    public void PinchCannotCarryASwipeDirection()
    {
        byte[] frame = CreateFrame(
            InputFrameType.Gesture,
            InputFrameFlags.Coalescible,
            12);
        frame[16] = (byte)GestureKind.Pinch;
        frame[17] = (byte)InputPhase.Update;
        frame[18] = (byte)GestureDirection.Up;
        BinaryPrimitives.WriteInt32LittleEndian(
            frame.AsSpan(20, 4),
            BitConverter.SingleToInt32Bits(1));

        ProtocolViolationException error = Assert.Throws<ProtocolViolationException>(
            () => InputFrameDecoder.Decode(frame));

        Assert.Contains("requires direction None", error.Message);
    }

    private static JsonDocument LoadFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "input-frames.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static byte[] CreateFrame(
        InputFrameType type,
        InputFrameFlags flags,
        int payloadBytes)
    {
        byte[] frame = new byte[16 + payloadBytes];
        frame[0] = 1;
        frame[1] = (byte)type;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2, 2), (ushort)flags);
        return frame;
    }

    private static InputFrameFlags ReadFlags(JsonElement flags)
    {
        InputFrameFlags value = InputFrameFlags.None;
        foreach (JsonElement flag in flags.EnumerateArray())
        {
            value |= Enum.Parse<InputFrameFlags>(flag.GetString()!, ignoreCase: true);
        }

        return value;
    }

    private static InputFrameType ReadType(string value) => value switch
    {
        "POINTER_DELTA" => InputFrameType.PointerDelta,
        "BUTTON" => InputFrameType.Button,
        "SCROLL" => InputFrameType.Scroll,
        "GESTURE" => InputFrameType.Gesture,
        "RELEASE_ALL" => InputFrameType.ReleaseAll,
        _ => throw new Xunit.Sdk.XunitException($"Unknown fixture frame type: {value}")
    };

    private static GestureKind ReadGesture(string value) => value switch
    {
        "PINCH" => GestureKind.Pinch,
        "ROTATE" => GestureKind.Rotate,
        "THREE_FINGER_SWIPE" => GestureKind.ThreeFingerSwipe,
        "FOUR_FINGER_SWIPE" => GestureKind.FourFingerSwipe,
        _ => throw new Xunit.Sdk.XunitException($"Unknown fixture gesture: {value}")
    };

    private static void AssertPayload(JsonElement expected, InputFrame actual)
    {
        switch (actual)
        {
            case PointerDeltaFrame pointer:
                Assert.Equal(expected.GetProperty("dx").GetSingle(), pointer.Dx);
                Assert.Equal(expected.GetProperty("dy").GetSingle(), pointer.Dy);
                Assert.Equal(expected.GetProperty("velocity").GetSingle(), pointer.Velocity);
                break;
            case ButtonFrame button:
                Assert.Equal(
                    Enum.Parse<InputButton>(expected.GetProperty("button").GetString()!, true),
                    button.Button);
                Assert.Equal(
                    Enum.Parse<ButtonAction>(expected.GetProperty("action").GetString()!, true),
                    button.Action);
                break;
            case ScrollFrame scroll:
                Assert.Equal(expected.GetProperty("dx").GetSingle(), scroll.Dx);
                Assert.Equal(expected.GetProperty("dy").GetSingle(), scroll.Dy);
                Assert.Equal(
                    Enum.Parse<InputPhase>(expected.GetProperty("phase").GetString()!, true),
                    scroll.Phase);
                break;
            case GestureFrame gesture:
                Assert.Equal(ReadGesture(expected.GetProperty("gesture").GetString()!), gesture.Gesture);
                Assert.Equal(
                    Enum.Parse<InputPhase>(expected.GetProperty("phase").GetString()!, true),
                    gesture.Phase);
                Assert.Equal(
                    Enum.Parse<GestureDirection>(
                        expected.GetProperty("direction").GetString()!,
                        true),
                    gesture.Direction);
                Assert.Equal(expected.GetProperty("value1").GetSingle(), gesture.Value1);
                Assert.Equal(expected.GetProperty("value2").GetSingle(), gesture.Value2);
                break;
            case ReleaseAllFrame:
                Assert.Empty(expected.EnumerateObject());
                break;
            default:
                throw new Xunit.Sdk.XunitException($"Unexpected frame type: {actual.GetType()}");
        }
    }
}
