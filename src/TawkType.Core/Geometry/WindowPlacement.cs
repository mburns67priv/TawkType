namespace TawkType.Core.Geometry;

/// <summary>A rectangle in physical screen pixels. Screen coordinates can be negative.</summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public long Area => (long)Math.Max(0, Width) * Math.Max(0, Height);

    /// <summary>The overlapping region, or a zero-sized rect when they do not touch.</summary>
    public PixelRect Intersect(PixelRect other)
    {
        var left = Math.Max(Left, other.Left);
        var top = Math.Max(Top, other.Top);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);

        return right <= left || bottom <= top
            ? new PixelRect(left, top, 0, 0)
            : new PixelRect(left, top, right - left, bottom - top);
    }
}

/// <summary>
/// Decides where a remembered window should actually go, given the monitors that exist right now.
///
/// The naive check — "is the point inside the virtual desktop?" — is wrong on multi-monitor setups,
/// because the virtual desktop is the *bounding box* of the monitors and can contain large regions
/// where no monitor exists. A window restored into one of those holes is invisible and cannot be
/// dragged back. Everything here works against the real monitor rectangles instead.
/// </summary>
public static class WindowPlacement
{
    /// <summary>
    /// The work area the window overlaps most, or null when it is effectively off-screen — the monitor
    /// it used to live on has been unplugged, switched off, or rearranged out from under it.
    /// </summary>
    public static PixelRect? FindHome(PixelRect window, IReadOnlyList<PixelRect> workAreas)
    {
        PixelRect? best = null;
        long bestArea = 0;

        foreach (var area in workAreas)
        {
            // Any overlap at all is enough to claim the window: Clamp then pulls it fully onto that
            // monitor. Demanding a minimum would throw away the monitor the user chose in exactly the
            // case where recovering it matters most — a window left hanging off the edge.
            var overlap = window.Intersect(area);
            if (overlap.Area > bestArea)
            {
                bestArea = overlap.Area;
                best = area;
            }
        }

        return best;
    }

    /// <summary>
    /// Nudges the window fully inside the work area. When the window is larger than the area it is
    /// pinned to the top-left, so its own top-left stays reachable.
    /// </summary>
    public static PixelRect Clamp(PixelRect window, PixelRect workArea)
    {
        var left = window.Left;
        var top = window.Top;

        if (window.Right > workArea.Right)
        {
            left = workArea.Right - window.Width;
        }

        if (window.Bottom > workArea.Bottom)
        {
            top = workArea.Bottom - window.Height;
        }

        left = Math.Max(left, workArea.Left);
        top = Math.Max(top, workArea.Top);

        return window with { Left = left, Top = top };
    }

    /// <summary>
    /// The whole restore decision: keep the remembered position when it is still reachable (nudged
    /// fully on-screen), otherwise return null so the caller falls back to its default placement.
    /// </summary>
    public static PixelRect? Restore(PixelRect remembered, IReadOnlyList<PixelRect> workAreas)
        => FindHome(remembered, workAreas) is { } home ? Clamp(remembered, home) : null;
}
