using HarmonyPcTouchpad.Agent.Protocol;

namespace HarmonyPcTouchpad.Agent.Core;

public enum InputSessionState
{
    Inactive,
    Active,
    Faulted
}

public sealed class InputSession
{
    private readonly IInputSink _sink;
    private uint _nextSequence;
    private bool _releasePending;

    public InputSession(IInputSink sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public InputSessionState State { get; private set; } = InputSessionState.Inactive;

    public void Begin()
    {
        if (State != InputSessionState.Inactive)
        {
            throw new InvalidOperationException(
                "The input session must be inactive before it can begin.");
        }

        _nextSequence = 0;
        _releasePending = false;
        State = InputSessionState.Active;
    }

    public void Process(InputFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (State != InputSessionState.Active)
        {
            throw new InvalidOperationException("The input session is not active.");
        }

        if (frame.Sequence != _nextSequence)
        {
            uint received = frame.Sequence;
            FailClosed();
            throw new ProtocolViolationException(
                $"Expected sequence {_nextSequence}, received {received}.");
        }

        try
        {
            Dispatch(frame);
            _nextSequence = unchecked(_nextSequence + 1);
        }
        catch
        {
            FailClosed();
            throw;
        }
    }

    public void Disconnect() => Deactivate();

    public void Timeout() => Deactivate();

    private void Dispatch(InputFrame frame)
    {
        switch (frame)
        {
            case PointerDeltaFrame pointer:
                _sink.MovePointer(pointer);
                break;
            case ButtonFrame button:
                _sink.SetButton(button.Button, button.Action);
                break;
            case ScrollFrame scroll:
                _sink.Scroll(scroll);
                break;
            case GestureFrame gesture:
                _sink.HandleGesture(gesture);
                break;
            case ReleaseAllFrame:
                _sink.ReleaseAll();
                break;
            default:
                throw new ProtocolViolationException(
                    $"Unsupported input frame implementation: {frame.GetType().Name}.");
        }
    }

    private void FailClosed()
    {
        if (State != InputSessionState.Active)
        {
            return;
        }

        State = InputSessionState.Faulted;
        try
        {
            _sink.ReleaseAll();
            _releasePending = false;
        }
        catch
        {
            _releasePending = true;
            throw;
        }
    }

    private void Deactivate()
    {
        if (State == InputSessionState.Inactive)
        {
            return;
        }

        if (State == InputSessionState.Faulted && !_releasePending)
        {
            State = InputSessionState.Inactive;
            return;
        }

        State = InputSessionState.Faulted;
        try
        {
            _sink.ReleaseAll();
            _releasePending = false;
            State = InputSessionState.Inactive;
        }
        catch
        {
            _releasePending = true;
            throw;
        }
    }
}
