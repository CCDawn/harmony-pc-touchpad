using HarmonyPcTouchpad.Agent.Core;
using HarmonyPcTouchpad.Agent.Protocol;

namespace HarmonyPcTouchpad.Agent.Windows;

public sealed class WindowsInputSink : IInputSink
{
    private static readonly InputButton[] ReleaseOrder =
        [InputButton.Left, InputButton.Right, InputButton.Middle];

    private readonly IWindowsInputApi _native;
    private readonly HashSet<InputButton> _heldButtons = [];
    private float _pointerRemainderX;
    private float _pointerRemainderY;
    private float _scrollRemainderX;
    private float _scrollRemainderY;

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
        throw new NotSupportedException(
            $"{frame.Gesture} is not mapped to a Windows input command yet.");
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

        if (failures is not null)
        {
            throw new AggregateException("One or more held buttons could not be released.", failures);
        }
    }

    private static int TakeWhole(ref float value)
    {
        int whole = checked((int)MathF.Truncate(value));
        value -= whole;
        return whole;
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
