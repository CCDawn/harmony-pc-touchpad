using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HarmonyPcTouchpad.Agent.Windows;

public enum WindowsInputCommandKind
{
    Move,
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    MiddleDown,
    MiddleUp,
    VerticalWheel,
    HorizontalWheel,
    KeyDown,
    KeyUp
}

public readonly record struct WindowsInputCommand(
    WindowsInputCommandKind Kind,
    int X = 0,
    int Y = 0,
    int WheelDelta = 0,
    ushort VirtualKey = 0);

public interface IWindowsInputApi
{
    void Send(WindowsInputCommand command);
}

public sealed partial class NativeWindowsInputApi : IWindowsInputApi
{
    public void Send(WindowsInputCommand command)
    {
        NativeInput input = command.Kind is
            WindowsInputCommandKind.KeyDown or
            WindowsInputCommandKind.KeyUp ?
            new NativeInput
            {
                Type = NativeMethods.InputKeyboard,
                Data = new NativeInputUnion
                {
                    Keyboard = new NativeKeyboardInput
                    {
                        VirtualKey = command.VirtualKey,
                        ScanCode = 0,
                        Flags = command.Kind == WindowsInputCommandKind.KeyUp ?
                            NativeMethods.KeyboardEventKeyUp :
                            0,
                        Time = 0,
                        ExtraInfo = 0
                    }
                }
            } :
            new NativeInput
            {
                Type = NativeMethods.InputMouse,
                Data = new NativeInputUnion
                {
                    Mouse = new NativeMouseInput
                    {
                        Dx = command.X,
                        Dy = command.Y,
                        MouseData = unchecked((uint)command.WheelDelta),
                        Flags = ReadMouseFlags(command.Kind)
                    }
                }
            };

        uint sent = NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeInput>());
        if (sent != 1)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"SendInput failed for {command.Kind}.");
        }
    }

    private static uint ReadMouseFlags(WindowsInputCommandKind kind) => kind switch
    {
        WindowsInputCommandKind.Move => NativeMethods.MouseEventMove,
        WindowsInputCommandKind.LeftDown => NativeMethods.MouseEventLeftDown,
        WindowsInputCommandKind.LeftUp => NativeMethods.MouseEventLeftUp,
        WindowsInputCommandKind.RightDown => NativeMethods.MouseEventRightDown,
        WindowsInputCommandKind.RightUp => NativeMethods.MouseEventRightUp,
        WindowsInputCommandKind.MiddleDown => NativeMethods.MouseEventMiddleDown,
        WindowsInputCommandKind.MiddleUp => NativeMethods.MouseEventMiddleUp,
        WindowsInputCommandKind.VerticalWheel => NativeMethods.MouseEventWheel,
        WindowsInputCommandKind.HorizontalWheel => NativeMethods.MouseEventHWheel,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeInputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)]
        public NativeMouseInput Mouse;

        [FieldOffset(0)]
        public NativeKeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    private static partial class NativeMethods
    {
        internal const uint InputMouse = 0;
        internal const uint InputKeyboard = 1;
        internal const uint MouseEventMove = 0x0001;
        internal const uint MouseEventLeftDown = 0x0002;
        internal const uint MouseEventLeftUp = 0x0004;
        internal const uint MouseEventRightDown = 0x0008;
        internal const uint MouseEventRightUp = 0x0010;
        internal const uint MouseEventMiddleDown = 0x0020;
        internal const uint MouseEventMiddleUp = 0x0040;
        internal const uint MouseEventWheel = 0x0800;
        internal const uint MouseEventHWheel = 0x1000;
        internal const uint KeyboardEventKeyUp = 0x0002;

        [LibraryImport("user32.dll", SetLastError = true)]
        internal static partial uint SendInput(
            uint inputCount,
            [In] NativeInput[] inputs,
            int inputSize);
    }
}
