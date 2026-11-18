using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Vespera;

internal sealed class NativeInput : IDisposable
{
    private const int WmInput = 0x00FF;
    private const uint InputSink = 0x00000100;
    private const uint RidInput = 0x10000003;
    private const uint RidHeader = 0x10000005;
    private const ushort WheelMask = 0x0C00;
    private const int ExtendedStyle = -20;
    private const int Transparent = 0x20;
    private const int VirtualKeyAlt = 0x12;
    private const uint MoveOnly = 0x0001 | 0x0004 | 0x0010;
    private readonly HwndSource source;
    private uint foregroundPid;
    private bool match;

    public event Action? KeyboardActivity;
    public event Action? WheelActivity;

    public NativeInput(HwndSource source)
    {
        this.source = source;
        var devices = new[]
        {
            new RawDevice { UsagePage = 1, Usage = 6, Flags = InputSink, Target = source.Handle },
            new RawDevice { UsagePage = 1, Usage = 2, Flags = InputSink, Target = source.Handle }
        };
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawDevice>()))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Оверлей не смог подписаться на активность клавиатуры и колеса.");
        source.AddHook(HandleInput);
    }

    private IntPtr HandleInput(IntPtr window, int message, IntPtr wParam, IntPtr input, ref bool handled)
    {
        if (message != WmInput) return IntPtr.Zero;
        uint headerSize = (uint)Marshal.SizeOf<RawHeader>();
        uint size = headerSize;
        if (ReadHeader(input, RidHeader, out RawHeader header, ref size, headerSize) != headerSize) return IntPtr.Zero;
        if (header.Type == 1)
        {
            // The header tells us that this was a keyboard event. Never read its payload.
            KeyboardActivity?.Invoke();
        }
        else if (header.Type == 0)
        {
            size = (uint)Marshal.SizeOf<MousePacket>();
            if (ReadMouse(input, RidInput, out MousePacket mouse, ref size, headerSize) == size &&
                (mouse.WheelFlags & WheelMask) != 0)
                WheelActivity?.Invoke();
        }
        return IntPtr.Zero;
    }

    public bool IsCodexForeground()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint processId);
        if (processId == 0) return false;
        if (processId == foregroundPid) return match;
        foregroundPid = processId;
        match = false;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            match = process.ProcessName.Equals("Codex", StringComparison.OrdinalIgnoreCase) ||
                (process.ProcessName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) &&
                 process.MainModule?.FileName.Contains(@"\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) == true);
        }
        catch (InvalidOperationException) { }
        catch (ArgumentException) { }
        catch (Win32Exception) { }
        return match;
    }

    public uint IdleMilliseconds
    {
        get
        {
            var input = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
            return GetLastInputInfo(ref input) ? unchecked((uint)Environment.TickCount - input.Time) : 0;
        }
    }

    public static bool TryGetPointer(out Point pointer)
    {
        bool success = GetCursorPos(out CursorPoint native);
        pointer = new Point(native.X, native.Y);
        return success;
    }

    public static bool AltHeld => GetAsyncKeyState(VirtualKeyAlt) < 0;

    public void MoveWindow(Point origin) =>
        SetWindowPos(source.Handle, IntPtr.Zero, (int)Math.Round(origin.X), (int)Math.Round(origin.Y), 0, 0, MoveOnly);

    public void SetClickThrough(bool enabled)
    {
        int style = GetWindowLong(source.Handle, ExtendedStyle);
        SetWindowLong(source.Handle, ExtendedStyle, enabled ? style | Transparent : style & ~Transparent);
    }

    public void Dispose() => source.RemoveHook(HandleInput);

    [StructLayout(LayoutKind.Sequential)]
    private struct RawDevice { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RawHeader { public uint Type, Size; public IntPtr Device, WParam; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MousePacket
    {
        public RawHeader Header;
        public ushort Flags, Padding, WheelFlags, WheelData;
        public uint RawButtons;
        public int X, Y;
        public uint Extra;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput { public uint Size, Time; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] RawDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", EntryPoint = "GetRawInputData")]
    private static extern uint ReadHeader(IntPtr input, uint flag, out RawHeader header, ref uint size, uint headerSize);
    [DllImport("user32.dll", EntryPoint = "GetRawInputData")]
    private static extern uint ReadMouse(IntPtr input, uint flag, out MousePacket mouse, ref uint size, uint headerSize);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput input);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr window, int index, int value);
}
