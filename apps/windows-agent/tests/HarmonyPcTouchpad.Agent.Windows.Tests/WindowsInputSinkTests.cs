using HarmonyPcTouchpad.Agent.Protocol;

namespace HarmonyPcTouchpad.Agent.Windows.Tests;

public sealed class WindowsInputSinkTests
{
    [Fact]
    public void FractionalPointerMovementIsPreservedAcrossFrames()
    {
        var native = new RecordingWindowsInputApi();
        var sink = new WindowsInputSink(native);

        sink.MovePointer(Pointer(0.4f, -0.4f));
        sink.MovePointer(Pointer(0.7f, -0.7f));

        Assert.Equal(
            [new WindowsInputCommand(WindowsInputCommandKind.Move, X: 1, Y: -1)],
            native.Commands);
    }

    [Fact]
    public void ReleaseAllReleasesEachHeldButtonExactlyOnce()
    {
        var native = new RecordingWindowsInputApi();
        var sink = new WindowsInputSink(native);

        sink.SetButton(InputButton.Left, ButtonAction.Down);
        sink.SetButton(InputButton.Left, ButtonAction.Down);
        sink.SetButton(InputButton.Right, ButtonAction.Down);
        sink.ReleaseAll();
        sink.ReleaseAll();

        Assert.Equal(
            [
                new WindowsInputCommand(WindowsInputCommandKind.LeftDown),
                new WindowsInputCommand(WindowsInputCommandKind.RightDown),
                new WindowsInputCommand(WindowsInputCommandKind.LeftUp),
                new WindowsInputCommand(WindowsInputCommandKind.RightUp)
            ],
            native.Commands);
    }

    [Fact]
    public void ScrollPreservesFractionalDeltasAndSeparatesAxes()
    {
        var native = new RecordingWindowsInputApi();
        var sink = new WindowsInputSink(native);

        sink.Scroll(Scroll(0.5f, -0.5f));
        sink.Scroll(Scroll(0.75f, -0.75f));

        Assert.Equal(
            [
                new WindowsInputCommand(WindowsInputCommandKind.HorizontalWheel, WheelDelta: 1),
                new WindowsInputCommand(WindowsInputCommandKind.VerticalWheel, WheelDelta: -1)
            ],
            native.Commands);
    }

    [Fact]
    public void PinchMapsToCtrlWheelAndReleasesControl()
    {
        var native = new RecordingWindowsInputApi();
        var sink = new WindowsInputSink(native);
        sink.HandleGesture(new GestureFrame(
            1,
            InputFrameFlags.None,
            0,
            1,
            GestureKind.Pinch,
            InputPhase.Begin,
            GestureDirection.None,
            1,
            0));
        sink.HandleGesture(new GestureFrame(
            1,
            InputFrameFlags.Coalescible,
            1,
            2,
            GestureKind.Pinch,
            InputPhase.Update,
            GestureDirection.None,
            1.2f,
            0));
        sink.HandleGesture(new GestureFrame(
            1,
            InputFrameFlags.Final,
            2,
            3,
            GestureKind.Pinch,
            InputPhase.End,
            GestureDirection.None,
            1,
            0));

        Assert.Equal(
            [
                new WindowsInputCommand(
                    WindowsInputCommandKind.KeyDown,
                    VirtualKey: 0x11),
                new WindowsInputCommand(
                    WindowsInputCommandKind.VerticalWheel,
                    WheelDelta: 120),
                new WindowsInputCommand(
                    WindowsInputCommandKind.KeyUp,
                    VirtualKey: 0x11)
            ],
            native.Commands);
    }

    [Fact]
    public void ThreeFingerSwipeMapsToTaskViewChord()
    {
        var native = new RecordingWindowsInputApi();
        var sink = new WindowsInputSink(native);

        sink.HandleGesture(new GestureFrame(
            1,
            InputFrameFlags.Final,
            0,
            1,
            GestureKind.ThreeFingerSwipe,
            InputPhase.End,
            GestureDirection.Up,
            100,
            1250));

        Assert.Equal(
            [
                new WindowsInputCommand(WindowsInputCommandKind.KeyDown, VirtualKey: 0x5B),
                new WindowsInputCommand(WindowsInputCommandKind.KeyDown, VirtualKey: 0x09),
                new WindowsInputCommand(WindowsInputCommandKind.KeyUp, VirtualKey: 0x09),
                new WindowsInputCommand(WindowsInputCommandKind.KeyUp, VirtualKey: 0x5B)
            ],
            native.Commands);
    }

    [Fact]
    public void FourFingerRightSwipeMapsToNextVirtualDesktopChord()
    {
        var native = new RecordingWindowsInputApi();
        var sink = new WindowsInputSink(native);

        sink.HandleGesture(new GestureFrame(
            1,
            InputFrameFlags.Final,
            0,
            1,
            GestureKind.FourFingerSwipe,
            InputPhase.End,
            GestureDirection.Right,
            100,
            1250));

        Assert.Equal(
            [
                new WindowsInputCommand(WindowsInputCommandKind.KeyDown, VirtualKey: 0x11),
                new WindowsInputCommand(WindowsInputCommandKind.KeyDown, VirtualKey: 0x5B),
                new WindowsInputCommand(WindowsInputCommandKind.KeyDown, VirtualKey: 0x27),
                new WindowsInputCommand(WindowsInputCommandKind.KeyUp, VirtualKey: 0x27),
                new WindowsInputCommand(WindowsInputCommandKind.KeyUp, VirtualKey: 0x5B),
                new WindowsInputCommand(WindowsInputCommandKind.KeyUp, VirtualKey: 0x11)
            ],
            native.Commands);
    }

    [Fact]
    public void ReleaseAllReleasesControlAfterInterruptedPinch()
    {
        var native = new RecordingWindowsInputApi();
        var sink = new WindowsInputSink(native);

        sink.HandleGesture(new GestureFrame(
            1,
            InputFrameFlags.None,
            0,
            1,
            GestureKind.Pinch,
            InputPhase.Begin,
            GestureDirection.None,
            1,
            0));
        sink.ReleaseAll();
        sink.ReleaseAll();

        Assert.Equal(
            [
                new WindowsInputCommand(WindowsInputCommandKind.KeyDown, VirtualKey: 0x11),
                new WindowsInputCommand(WindowsInputCommandKind.KeyUp, VirtualKey: 0x11)
            ],
            native.Commands);
    }

    [Fact]
    public void RotateRemainsExplicitlyUnsupported()
    {
        var native = new RecordingWindowsInputApi();
        var sink = new WindowsInputSink(native);
        var gesture = new GestureFrame(
            1,
            InputFrameFlags.Final,
            0,
            1,
            GestureKind.Rotate,
            InputPhase.End,
            GestureDirection.None,
            1,
            0);

        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => sink.HandleGesture(gesture));

        Assert.Contains("not mapped", error.Message);
        Assert.Empty(native.Commands);
    }

    [Fact]
    public void ReleaseAllAttemptsRemainingButtonsAfterANativeFailure()
    {
        var native = new RecordingWindowsInputApi();
        var sink = new WindowsInputSink(native);
        sink.SetButton(InputButton.Left, ButtonAction.Down);
        sink.SetButton(InputButton.Right, ButtonAction.Down);
        native.FailOn = WindowsInputCommandKind.LeftUp;

        AggregateException error = Assert.Throws<AggregateException>(() => sink.ReleaseAll());

        Assert.Single(error.InnerExceptions);
        Assert.Contains(
            new WindowsInputCommand(WindowsInputCommandKind.RightUp),
            native.Commands);
    }

    private static PointerDeltaFrame Pointer(float dx, float dy) =>
        new(1, InputFrameFlags.Coalescible, 0, 1, dx, dy, 0);

    private static ScrollFrame Scroll(float dx, float dy) =>
        new(1, InputFrameFlags.Coalescible, 0, 1, dx, dy, InputPhase.Update);

    private sealed class RecordingWindowsInputApi : IWindowsInputApi
    {
        public List<WindowsInputCommand> Commands { get; } = [];

        public WindowsInputCommandKind? FailOn { get; set; }

        public void Send(WindowsInputCommand command)
        {
            Commands.Add(command);
            if (command.Kind == FailOn)
            {
                throw new InvalidOperationException("Synthetic native failure.");
            }
        }
    }
}
