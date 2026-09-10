using System.Runtime.InteropServices;
using Talk2Me.Core.Geometry;

namespace Talk2Me.Windows.Display;

/// <summary>
/// The monitors as Windows currently sees them, in physical pixels.
///
/// Everything here deliberately works in physical pixels rather than WPF's device-independent units:
/// monitor rectangles come from Win32 in physical pixels, and on a mixed-DPI desktop converting between
/// the two needs the DPI of the monitor you are moving *to*, which you do not know until you are there.
/// Saving and restoring positions through <see cref="GetBounds"/> and <see cref="MoveTo"/> sidesteps the
/// conversion entirely.
/// </summary>
public static class MonitorLayout
{
    private const uint MonitorDefaultToNull = 0;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    /// <summary>Every connected monitor's work area — the screen minus the taskbar.</summary>
    public static IReadOnlyList<PixelRect> WorkAreas()
    {
        var areas = new List<PixelRect>();

        EnumDisplayMonitors(nint.Zero, nint.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                areas.Add(ToRect(info.Work));
            }

            return true;
        }, nint.Zero);

        return areas;
    }

    /// <summary>The window's rectangle in physical pixels, or null if the handle is not live yet.</summary>
    public static PixelRect? GetBounds(nint handle)
    {
        if (handle == 0 || !GetWindowRect(handle, out var rect))
        {
            return null;
        }

        return ToRect(rect);
    }

    /// <summary>
    /// Moves the window without resizing, re-ordering or activating it. SWP_NOACTIVATE matters: the
    /// status bar must never take focus, even when it is being put back on screen.
    /// </summary>
    public static void MoveTo(nint handle, int left, int top)
    {
        if (handle != 0)
        {
            SetWindowPos(handle, nint.Zero, left, top, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }
    }

    private static PixelRect ToRect(Rect rect)
        => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, nint clip, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint handle, nint after, int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }
}
