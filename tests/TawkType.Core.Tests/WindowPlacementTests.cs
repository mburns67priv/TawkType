using TawkType.Core.Geometry;

namespace TawkType.Core.Tests;

public sealed class WindowPlacementTests
{
    // The owner's actual four-monitor arrangement. Note the holes: the bounding box of these four
    // spans -1920..4480 x -1080..1440, but large parts of it have no monitor behind them.
    private static readonly PixelRect Primary = new(0, 0, 2560, 1440);
    private static readonly PixelRect RightOfPrimary = new(2560, 0, 1920, 1080);
    private static readonly PixelRect AbovePrimary = new(404, -1080, 1920, 1080);
    private static readonly PixelRect LeftOfPrimary = new(-1920, 0, 1920, 1080);

    private static readonly PixelRect[] AllFour = [Primary, RightOfPrimary, AbovePrimary, LeftOfPrimary];

    private static PixelRect Bar(int left, int top) => new(left, top, 433, 117);

    [Fact]
    public void Keeps_a_position_that_is_still_on_a_monitor()
    {
        var remembered = Bar(3000, 400); // on the monitor right of primary

        Assert.Equal(remembered, WindowPlacement.Restore(remembered, AllFour));
    }

    [Fact]
    public void Drops_the_position_when_that_monitor_is_gone()
    {
        var remembered = Bar(3000, 400);

        // Unplug the monitor it was on.
        Assert.Null(WindowPlacement.Restore(remembered, [Primary, AbovePrimary, LeftOfPrimary]));
    }

    [Fact]
    public void Drops_a_position_left_stranded_in_a_hole_in_the_virtual_desktop()
    {
        // This is the case a virtual-desktop bounding-box check gets wrong. With only the top and
        // left monitors alive, the bounding box still spans -1920..2324 x -1080..1080, so 1000,500
        // looks "inside the desktop" — but no monitor covers it.
        var stranded = Bar(1000, 500);
        PixelRect[] remaining = [AbovePrimary, LeftOfPrimary];

        var boundingBoxWouldAccept =
            stranded.Left > -1920 && stranded.Top > -1080 && stranded.Left < 2324 && stranded.Top < 1080;

        Assert.True(boundingBoxWouldAccept, "the naive check accepts it, which is the whole point");
        Assert.Null(WindowPlacement.Restore(stranded, remaining));
    }

    [Fact]
    public void Pulls_a_window_that_only_overlaps_by_a_sliver_fully_back_on()
    {
        // Two pixels of a 433px bar poking onto the primary. That monitor is still the right home for
        // it, so it is pulled fully on rather than being sent back to the default corner.
        var almostOff = Bar(-433 + 2, 0);

        var placed = Assert.NotNull(WindowPlacement.Restore(almostOff, [Primary]));

        Assert.Equal(Primary.Left, placed.Left);
        Assert.Equal(Primary.Top, placed.Top);
    }

    [Fact]
    public void Pulls_a_window_hanging_off_the_edge_back_inside()
    {
        // Saved when the monitor was wider, or nudged off the bottom.
        var overhanging = Bar(2400, 1400);

        var placed = Assert.NotNull(WindowPlacement.Restore(overhanging, [Primary]));

        Assert.Equal(Primary.Right - 433, placed.Left);
        Assert.Equal(Primary.Bottom - 117, placed.Top);
    }

    [Fact]
    public void Chooses_the_monitor_the_window_mostly_sits_on()
    {
        // Straddling primary and the one to its right, with most of it on the right-hand monitor.
        var straddling = new PixelRect(2460, 300, 400, 200);

        var home = WindowPlacement.FindHome(straddling, AllFour);

        Assert.Equal(RightOfPrimary, home);
    }

    [Fact]
    public void A_window_larger_than_the_screen_keeps_its_top_left_reachable()
    {
        var huge = new PixelRect(-500, -500, 4000, 3000);

        var placed = WindowPlacement.Clamp(huge, Primary);

        Assert.Equal(Primary.Left, placed.Left);
        Assert.Equal(Primary.Top, placed.Top);
    }

    [Fact]
    public void Restore_returns_null_when_there_are_no_monitors_at_all()
    {
        Assert.Null(WindowPlacement.Restore(Bar(100, 100), []));
    }

    [Theory]
    [InlineData(0, 0, 100, 100, 50, 50, 100, 100, 50, 50)]   // overlapping corner
    [InlineData(0, 0, 100, 100, 200, 200, 50, 50, 0, 0)]     // disjoint
    [InlineData(0, 0, 100, 100, 0, 100, 100, 50, 0, 0)]      // edge-to-edge counts as no overlap
    public void Intersect_reports_the_shared_region(
        int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh, int expectedWidth, int expectedHeight)
    {
        var overlap = new PixelRect(ax, ay, aw, ah).Intersect(new PixelRect(bx, by, bw, bh));

        Assert.Equal(expectedWidth, overlap.Width);
        Assert.Equal(expectedHeight, overlap.Height);
    }
}
