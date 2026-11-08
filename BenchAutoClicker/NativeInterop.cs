using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace BenchAutoClicker;

internal static class NativeInterop
{
    public const int WmHotKey = 0x0312;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;

    private const uint InputMouse = 0;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventVirtualDesk = 0x4000;
    private const uint MouseEventAbsolute = 0x8000;

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint GaRoot = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int size);

    public static void ApplyDarkWindowFrame(IntPtr windowHandle) => ApplyWindowFrame(windowHandle, true);

    public static void ApplyWindowFrame(IntPtr windowHandle, bool isDark)
    {
        var enabled = isDark ? 1 : 0;
        // Unsupported attributes return HRESULT and preserve the normal frame on older Windows.
        _ = DwmSetWindowAttribute(windowHandle, 20, ref enabled, sizeof(int));
        var roundedCorners = 2;
        _ = DwmSetWindowAttribute(windowHandle, 33, ref roundedCorners, sizeof(int));
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MONITORINFO monitorInfo);

    public static Point GetCursorPosition()
    {
        if (!GetCursorPos(out var point))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), ErrorText(
                "Не удалось определить позицию курсора.", "The cursor position could not be read."));
        }

        return new Point(point.X, point.Y);
    }

    public static ClickResult TryClick(ClickConfiguration configuration)
    {
        var original = GetCursorPosition();
        var target = configuration.TargetMode == TargetMode.FixedPoint
            ? configuration.FixedPoint
            : original;

        if (configuration.JitterRadius > 0)
        {
            target = AddJitter(target, configuration.JitterRadius);
        }

        target = ClampToRealMonitor(target);
        var targetWindow = GetAncestor(WindowFromPoint(new POINT(target.X, target.Y)), GaRoot);
        GetWindowThreadProcessId(targetWindow, out var targetProcessId);
        if (targetProcessId == (uint)Environment.ProcessId ||
            (configuration.ExcludedWindowHandle != IntPtr.Zero &&
             targetWindow == GetAncestor(configuration.ExcludedWindowHandle, GaRoot)))
        {
            return ClickResult.Blocked(target);
        }

        var inputs = new List<INPUT>(8);
        var moved = target != original;
        if (moved)
        {
            inputs.Add(CreateMove(target));
        }

        var (down, up, physicalClicks) = configuration.MouseAction switch
        {
            MouseAction.Right => (MouseEventRightDown, MouseEventRightUp, 1),
            MouseAction.Middle => (MouseEventMiddleDown, MouseEventMiddleUp, 1),
            MouseAction.DoubleLeft => (MouseEventLeftDown, MouseEventLeftUp, 2),
            _ => (MouseEventLeftDown, MouseEventLeftUp, 1)
        };

        inputs.Add(CreateButton(down));
        inputs.Add(CreateButton(up));
        if (configuration.MouseAction == MouseAction.DoubleLeft)
        {
            inputs.Add(CreateButton(down));
            inputs.Add(CreateButton(up));
        }

        if (moved)
        {
            inputs.Add(CreateMove(original));
        }

        var inputArray = inputs.ToArray();
        var sent = SendInput((uint)inputArray.Length, inputArray, Marshal.SizeOf<INPUT>());
        if (sent != (uint)inputArray.Length)
        {
            var error = Marshal.GetLastWin32Error();
            RecoverAfterPartialInput(inputArray, sent, down, up, moved, original);
            throw new Win32Exception(error, ErrorText(
                "Windows не приняла команду клика. Возможно, целевое окно запущено от администратора.",
                "Windows did not accept the click. The target app may be running as administrator."));
        }

        return ClickResult.Sent(target, physicalClicks);
    }

    private static string ErrorText(string russian, string english) =>
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? russian : english;

    private static Point AddJitter(Point point, int radius)
    {
        var angle = Random.Shared.NextDouble() * Math.PI * 2;
        var distance = Math.Sqrt(Random.Shared.NextDouble()) * radius;
        return new Point(
            point.X + (int)Math.Round(Math.Cos(angle) * distance),
            point.Y + (int)Math.Round(Math.Sin(angle) * distance));
    }

    private static Point ClampToRealMonitor(Point point)
    {
        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        var width = Math.Max(1, GetSystemMetrics(SmCxVirtualScreen));
        var height = Math.Max(1, GetSystemMetrics(SmCyVirtualScreen));
        var virtualDesktopPoint = new Point(
            Math.Clamp(point.X, left, left + width - 1),
            Math.Clamp(point.Y, top, top + height - 1));

        var nativePoint = new POINT(virtualDesktopPoint.X, virtualDesktopPoint.Y);
        if (MonitorFromPoint(nativePoint, 0) != IntPtr.Zero)
        {
            return virtualDesktopPoint;
        }

        var nearestMonitor = MonitorFromPoint(nativePoint, 2);
        var monitorInfo = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (nearestMonitor == IntPtr.Zero || !GetMonitorInfo(nearestMonitor, ref monitorInfo))
        {
            return virtualDesktopPoint;
        }

        return new Point(
            Math.Clamp(virtualDesktopPoint.X, monitorInfo.Monitor.Left, monitorInfo.Monitor.Right - 1),
            Math.Clamp(virtualDesktopPoint.Y, monitorInfo.Monitor.Top, monitorInfo.Monitor.Bottom - 1));
    }

    private static void RecoverAfterPartialInput(
        INPUT[] attemptedInputs,
        uint sentCount,
        uint downFlag,
        uint upFlag,
        bool moved,
        Point original)
    {
        try
        {
            var buttonIsDown = false;
            for (var index = 0; index < sentCount; index++)
            {
                var flags = attemptedInputs[index].Data.Mouse.Flags;
                if ((flags & downFlag) != 0)
                {
                    buttonIsDown = true;
                }
                if ((flags & upFlag) != 0)
                {
                    buttonIsDown = false;
                }
            }

            var recovery = new List<INPUT>(2);
            if (buttonIsDown)
            {
                recovery.Add(CreateButton(upFlag));
            }
            if (moved && sentCount > 0)
            {
                recovery.Add(CreateMove(ClampToRealMonitor(original)));
            }

            if (recovery.Count == 0)
            {
                return;
            }

            var inputs = recovery.ToArray();
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }
        catch
        {
            // Preserve the original SendInput error.
        }
    }

    private static INPUT CreateMove(Point point)
    {
        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        var width = Math.Max(2, GetSystemMetrics(SmCxVirtualScreen));
        var height = Math.Max(2, GetSystemMetrics(SmCyVirtualScreen));

        var normalizedX = (int)Math.Round((point.X - left) * 65535d / (width - 1));
        var normalizedY = (int)Math.Round((point.Y - top) * 65535d / (height - 1));
        return new INPUT
        {
            Type = InputMouse,
            Data = new InputUnion
            {
                Mouse = new MOUSEINPUT
                {
                    Dx = normalizedX,
                    Dy = normalizedY,
                    Flags = MouseEventMove | MouseEventAbsolute | MouseEventVirtualDesk
                }
            }
        };
    }

    private static INPUT CreateButton(uint flags)
    {
        return new INPUT
        {
            Type = InputMouse,
            Data = new InputUnion { Mouse = new MOUSEINPUT { Flags = flags } }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct POINT(int x, int y)
    {
        public readonly int X = x;
        public readonly int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int Size;
        public RECT Monitor;
        public RECT WorkArea;
        public uint Flags;
    }
}
