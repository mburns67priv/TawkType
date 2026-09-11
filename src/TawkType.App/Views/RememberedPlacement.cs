using System.Windows;
using System.Windows.Interop;
using TawkType.Core.Geometry;
using TawkType.Windows.Display;

namespace TawkType.Desktop.Views;

/// <summary>
/// Restores a window to where the user last put it, but only if that place still exists.
///
/// Positions are stored in physical pixels, taken straight from the window handle, so no
/// device-independent-unit conversion is involved and mixed-DPI desktops need no special case.
/// </summary>
public static class RememberedPlacement
{
    /// <summary>The window's current top-left in physical pixels, for saving.</summary>
    public static (double Left, double Top)? Capture(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return MonitorLayout.GetBounds(handle) is { } bounds ? (bounds.Left, bounds.Top) : null;
    }

    /// <summary>
    /// Puts the window back at <paramref name="left"/>, <paramref name="top"/>, nudged fully onto
    /// whichever monitor it overlaps. Returns false when no monitor covers that spot any more — the
    /// caller should then fall back to its own default placement.
    /// </summary>
    public static bool TryRestore(Window window, double left, double top)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (MonitorLayout.GetBounds(handle) is not { } bounds)
        {
            return false; // no handle yet; the caller retries once the window is sourced
        }

        var wanted = bounds with { Left = (int)Math.Round(left), Top = (int)Math.Round(top) };

        if (WindowPlacement.Restore(wanted, MonitorLayout.WorkAreas()) is not { } placed)
        {
            return false;
        }

        MonitorLayout.MoveTo(handle, placed.Left, placed.Top);
        return true;
    }

    /// <summary>
    /// Pulls a window fully back onto a monitor if it is hanging off one, and reports false when it is
    /// on no monitor at all. Used after the displays change.
    /// </summary>
    public static bool EnsureOnScreen(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (MonitorLayout.GetBounds(handle) is not { } bounds)
        {
            return true; // nothing on screen yet, so nothing to rescue
        }

        if (WindowPlacement.Restore(bounds, MonitorLayout.WorkAreas()) is not { } placed)
        {
            return false;
        }

        if (placed.Left != bounds.Left || placed.Top != bounds.Top)
        {
            MonitorLayout.MoveTo(handle, placed.Left, placed.Top);
        }

        return true;
    }
}
