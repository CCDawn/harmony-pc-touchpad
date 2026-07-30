using HarmonyPcTouchpad.Agent.Protocol;

namespace HarmonyPcTouchpad.Agent.Core.Tests;

public sealed class InputSessionTests
{
    [Fact]
    public void DisconnectAfterButtonDownReleasesAllInput()
    {
        var sink = new RecordingInputSink();
        var session = new InputSession(sink);
        session.Begin();

        session.Process(new ButtonFrame(
            1,
            InputFrameFlags.None,
            0,
            1,
            InputButton.Left,
            ButtonAction.Down));
        session.Disconnect();

        Assert.Equal(["Button:Left:Down", "ReleaseAll"], sink.Events);
        Assert.Equal(InputSessionState.Inactive, session.State);
    }

    [Fact]
    public void SequenceGapFailsClosedAndReleasesAllInput()
    {
        var sink = new RecordingInputSink();
        var session = new InputSession(sink);
        session.Begin();

        ProtocolViolationException error = Assert.Throws<ProtocolViolationException>(
            () => session.Process(new PointerDeltaFrame(
                1,
                InputFrameFlags.Coalescible,
                1,
                1,
                1,
                1,
                1)));

        Assert.Contains("Expected sequence 0", error.Message);
        Assert.Equal(["ReleaseAll"], sink.Events);
        Assert.Equal(InputSessionState.Faulted, session.State);
    }

    [Fact]
    public void TimeoutIsIdempotentAfterTheSessionHasAlreadyFailedClosed()
    {
        var sink = new RecordingInputSink();
        var session = new InputSession(sink);
        session.Begin();

        Assert.Throws<ProtocolViolationException>(
            () => session.Process(new ReleaseAllFrame(
                1,
                InputFrameFlags.Final,
                3,
                1)));
        session.Timeout();

        Assert.Equal(["ReleaseAll"], sink.Events);
        Assert.Equal(InputSessionState.Inactive, session.State);
    }

    [Fact]
    public void SinkFailureFailsClosedBeforeItEscapes()
    {
        var sink = new RecordingInputSink { FailPointerMovement = true };
        var session = new InputSession(sink);
        session.Begin();

        Assert.Throws<InvalidOperationException>(
            () => session.Process(new PointerDeltaFrame(
                1,
                InputFrameFlags.Coalescible,
                0,
                1,
                1,
                1,
                1)));

        Assert.Equal(["ReleaseAll"], sink.Events);
        Assert.Equal(InputSessionState.Faulted, session.State);
    }

    [Fact]
    public void FailedDisconnectReleaseRemainsRetryable()
    {
        var sink = new RecordingInputSink { ReleaseFailuresRemaining = 1 };
        var session = new InputSession(sink);
        session.Begin();
        session.Process(new ButtonFrame(
            1,
            InputFrameFlags.None,
            0,
            1,
            InputButton.Left,
            ButtonAction.Down));

        Assert.Throws<InvalidOperationException>(() => session.Disconnect());
        Assert.Equal(InputSessionState.Faulted, session.State);

        session.Timeout();

        Assert.Equal(
            ["Button:Left:Down", "ReleaseAll", "ReleaseAll"],
            sink.Events);
        Assert.Equal(InputSessionState.Inactive, session.State);
    }

    private sealed class RecordingInputSink : IInputSink
    {
        public List<string> Events { get; } = [];

        public bool FailPointerMovement { get; init; }

        public int ReleaseFailuresRemaining { get; set; }

        public void MovePointer(PointerDeltaFrame frame)
        {
            if (FailPointerMovement)
            {
                throw new InvalidOperationException("Synthetic sink failure.");
            }

            Events.Add($"Move:{frame.Dx}:{frame.Dy}");
        }

        public void SetButton(InputButton button, ButtonAction action) =>
            Events.Add($"Button:{button}:{action}");

        public void Scroll(ScrollFrame frame) =>
            Events.Add($"Scroll:{frame.Dx}:{frame.Dy}:{frame.Phase}");

        public void HandleGesture(GestureFrame frame) =>
            Events.Add($"Gesture:{frame.Gesture}:{frame.Direction}");

        public void ReleaseAll()
        {
            Events.Add("ReleaseAll");
            if (ReleaseFailuresRemaining > 0)
            {
                ReleaseFailuresRemaining--;
                throw new InvalidOperationException("Synthetic release failure.");
            }
        }
    }
}
