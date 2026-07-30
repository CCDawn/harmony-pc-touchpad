namespace HarmonyPcTouchpad.Agent.Protocol;

public enum InputFrameType : byte
{
    PointerDelta = 1,
    Button = 2,
    Scroll = 3,
    Gesture = 4,
    ReleaseAll = 5
}

[Flags]
public enum InputFrameFlags : ushort
{
    None = 0,
    Coalescible = 1,
    Final = 2
}

public enum InputButton : byte
{
    Left = 1,
    Right = 2,
    Middle = 3
}

public enum ButtonAction : byte
{
    Down = 1,
    Up = 2
}

public enum InputPhase : byte
{
    Begin = 1,
    Update = 2,
    End = 3,
    Cancel = 4
}

public enum GestureKind : byte
{
    Pinch = 1,
    Rotate = 2,
    ThreeFingerSwipe = 3,
    FourFingerSwipe = 4
}

public enum GestureDirection : byte
{
    None = 0,
    Up = 1,
    Down = 2,
    Left = 3,
    Right = 4
}

public abstract record InputFrame(
    byte Version,
    InputFrameType Type,
    InputFrameFlags Flags,
    uint Sequence,
    ulong TimestampUs);

public sealed record PointerDeltaFrame(
    byte Version,
    InputFrameFlags Flags,
    uint Sequence,
    ulong TimestampUs,
    float Dx,
    float Dy,
    float Velocity)
    : InputFrame(Version, InputFrameType.PointerDelta, Flags, Sequence, TimestampUs);

public sealed record ButtonFrame(
    byte Version,
    InputFrameFlags Flags,
    uint Sequence,
    ulong TimestampUs,
    InputButton Button,
    ButtonAction Action)
    : InputFrame(Version, InputFrameType.Button, Flags, Sequence, TimestampUs);

public sealed record ScrollFrame(
    byte Version,
    InputFrameFlags Flags,
    uint Sequence,
    ulong TimestampUs,
    float Dx,
    float Dy,
    InputPhase Phase)
    : InputFrame(Version, InputFrameType.Scroll, Flags, Sequence, TimestampUs);

public sealed record GestureFrame(
    byte Version,
    InputFrameFlags Flags,
    uint Sequence,
    ulong TimestampUs,
    GestureKind Gesture,
    InputPhase Phase,
    GestureDirection Direction,
    float Value1,
    float Value2)
    : InputFrame(Version, InputFrameType.Gesture, Flags, Sequence, TimestampUs);

public sealed record ReleaseAllFrame(
    byte Version,
    InputFrameFlags Flags,
    uint Sequence,
    ulong TimestampUs)
    : InputFrame(Version, InputFrameType.ReleaseAll, Flags, Sequence, TimestampUs);
