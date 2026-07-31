using HarmonyPcTouchpad.Agent.Core;
using HarmonyPcTouchpad.Agent.Protocol;

namespace HarmonyPcTouchpad.Agent.Windows;

public sealed class WindowsInputSink : IInputSink
{
    private const ushort ControlKey = 0x11;
    private const ushort ShiftKey = 0x10;
    private const ushort AltKey = 0x12;
    private const ushort TabKey = 0x09;
    private const ushort WindowsKey = 0x5B;
    private const ushort LetterDKey = 0x44;
    private const ushort LeftKey = 0x25;
    private const ushort RightKey = 0x27;
    private const int WindowsWheelDelta = 120;
    private const float PinchNotchesPerLogUnit = 6f;

    private static readonly InputButton[] ReleaseOrder =
        [InputButton.Left, InputButton.Right, InputButton.Middle];

    private readonly IWindowsInputApi _native;
    private readonly HashSet<InputButton> _heldButtons = [];
    private float _pointerRemainderX;
    private float _pointerRemainderY;
    private float _scrollRemainderX;
    private float _scrollRemainderY;
    private float _pinchWheelRemainder;
    private bool _pinchControlHeld;

    public WindowsInputSink(IWindowsInputApi native)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
    }

    public void MovePointer(PointerDeltaFrame frame)
    {
        _pointerRemainderX += frame.Dx;
        _pointerRemainderY += frame.Dy;
        int x = TakeWhole(ref _pointerRemainderX);
        int y = TakeWhole(ref _pointerRemainderY);
        if (x != 0 || y != 0)
        {
            _native.Send(new(WindowsInputCommandKind.Move, X: x, Y: y));
        }
    }

    public void SetButton(InputButton button, ButtonAction action)
    {
        if (action == ButtonAction.Down)
        {
            if (_heldButtons.Contains(button))
            {
                return;
            }

            _native.Send(new(ReadButtonCommand(button, action)));
            _heldButtons.Add(button);
            return;
        }

        if (!_heldButtons.Contains(button))
        {
            return;
        }

        _native.Send(new(ReadButtonCommand(button, action)));
        _heldButtons.Remove(button);
    }

    public void Scroll(ScrollFrame frame)
    {
        _scrollRemainderX += frame.Dx;
        _scrollRemainderY += frame.Dy;
        int horizontal = TakeWhole(ref _scrollRemainderX);
        int vertical = TakeWhole(ref _scrollRemainderY);

        if (horizontal != 0)
        {
            _native.Send(new(
                WindowsInputCommandKind.HorizontalWheel,
                WheelDelta: horizontal));
        }

        if (vertical != 0)
        {
            _native.Send(new(
                WindowsInputCommandKind.VerticalWheel,
                WheelDelta: vertical));
        }
    }

    public void HandleGesture(GestureFrame frame)
    {
        switch (frame.Gesture)
        {
            case GestureKind.Pinch:
                HandlePinch(frame);
                return;
            case GestureKind.Rotate:
                throw new NotSupportedException(
                    $"{frame.Gesture} is not mapped to a Windows input command yet.");
            case GestureKind.ThreeFingerSwipe:
                HandleThreeFingerSwipe(frame);
                return;
            case GestureKind.FourFingerSwipe:
                HandleFourFingerSwipe(frame);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(frame.Gesture));
        }
    }

    public void ReleaseAll()
    {
        List<Exception>? failures = null;
        foreach (InputButton button in ReleaseOrder)
        {
            if (!_heldButtons.Contains(button))
            {
                continue;
            }

            try
            {
                _native.Send(new(ReadButtonCommand(button, ButtonAction.Up)));
                _heldButtons.Remove(button);
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        _pointerRemainderX = 0;
        _pointerRemainderY = 0;
        _scrollRemainderX = 0;
        _scrollRemainderY = 0;
        _pinchWheelRemainder = 0;

        if (_pinchControlHeld)
        {
            try
            {
                _native.Send(new(
                    WindowsInputCommandKind.KeyUp,
                    VirtualKey: ControlKey));
                _pinchControlHeld = false;
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more held inputs could not be released.", failures);
        }
    }

    private static int TakeWhole(ref float value)
    {
        int whole = checked((int)MathF.Truncate(value));
        value -= whole;
        return whole;
    }

    private void HandlePinch(GestureFrame frame)
    {
        switch (frame.Phase)
        {
            case InputPhase.Begin:
                EnsurePinchControlHeld();
                _pinchWheelRemainder = 0;
                return;
            case InputPhase.Update:
                EnsurePinchControlHeld();
                _pinchWheelRemainder +=
                    MathF.Log(frame.Value1) * PinchNotchesPerLogUnit;
                int notches = TakeWhole(ref _pinchWheelRemainder);
                if (notches != 0)
                {
                    _native.Send(new(
                        WindowsInputCommandKind.VerticalWheel,
                        WheelDelta: checked(notches * WindowsWheelDelta)));
                }
                return;
            case InputPhase.End:
            case InputPhase.Cancel:
                ReleasePinchControl();
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(frame.Phase));
        }
    }

    private void HandleThreeFingerSwipe(GestureFrame frame)
    {
        if (frame.Phase != InputPhase.End)
        {
            throw new ProtocolViolationException(
                "Three-finger swipe must end in one final frame.");
        }

        switch (frame.Direction)
        {
            case GestureDirection.Up:
                SendChord(WindowsKey, TabKey);
                return;
            case GestureDirection.Down:
                SendChord(WindowsKey, LetterDKey);
                return;
            case GestureDirection.Left:
                SendChord(AltKey, ShiftKey, TabKey);
                return;
            case GestureDirection.Right:
                SendChord(AltKey, TabKey);
                return;
            default:
                throw new ProtocolViolationException(
                    "Three-finger swipe direction is required.");
        }
    }

    private void HandleFourFingerSwipe(GestureFrame frame)
    {
        if (frame.Phase != InputPhase.End)
        {
            throw new ProtocolViolationException(
                "Four-finger swipe must end in one final frame.");
        }

        switch (frame.Direction)
        {
            case GestureDirection.Left:
                SendChord(ControlKey, WindowsKey, LeftKey);
                return;
            case GestureDirection.Right:
                SendChord(ControlKey, WindowsKey, RightKey);
                return;
            case GestureDirection.Up:
            case GestureDirection.Down:
                return;
            default:
                throw new ProtocolViolationException(
                    "Four-finger swipe direction is required.");
        }
    }

    private void EnsurePinchControlHeld()
    {
        if (_pinchControlHeld)
        {
            return;
        }

        _native.Send(new(
            WindowsInputCommandKind.KeyDown,
            VirtualKey: ControlKey));
        _pinchControlHeld = true;
    }

    private void ReleasePinchControl()
    {
        if (!_pinchControlHeld)
        {
            _pinchWheelRemainder = 0;
            return;
        }

        _native.Send(new(
            WindowsInputCommandKind.KeyUp,
            VirtualKey: ControlKey));
        _pinchControlHeld = false;
        _pinchWheelRemainder = 0;
    }

    private void SendChord(params ushort[] keys)
    {
        foreach (ushort key in keys)
        {
            _native.Send(new(
                WindowsInputCommandKind.KeyDown,
                VirtualKey: key));
        }

        for (int index = keys.Length - 1; index >= 0; index--)
        {
            _native.Send(new(
                WindowsInputCommandKind.KeyUp,
                VirtualKey: keys[index]));
        }
    }

    private static WindowsInputCommandKind ReadButtonCommand(
        InputButton button,
        ButtonAction action) =>
        (button, action) switch
        {
            (InputButton.Left, ButtonAction.Down) => WindowsInputCommandKind.LeftDown,
            (InputButton.Left, ButtonAction.Up) => WindowsInputCommandKind.LeftUp,
            (InputButton.Right, ButtonAction.Down) => WindowsInputCommandKind.RightDown,
            (InputButton.Right, ButtonAction.Up) => WindowsInputCommandKind.RightUp,
            (InputButton.Middle, ButtonAction.Down) => WindowsInputCommandKind.MiddleDown,
            (InputButton.Middle, ButtonAction.Up) => WindowsInputCommandKind.MiddleUp,
            _ => throw new ArgumentOutOfRangeException(
                nameof(button),
                button,
                $"Unsupported button/action pair: {button}/{action}.")
        };
}
