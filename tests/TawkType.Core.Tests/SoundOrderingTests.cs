using TawkType.Core.Settings;

namespace TawkType.Core.Tests;

/// <summary>
/// How the sound picker is ordered. Both rules exist because of what the real list looked like when
/// it was first opened: forty-odd events sorted plainly, which put "Alarm 1, Alarm 10, Alarm 2" at
/// the top and the chimes anybody would choose somewhere past the middle.
/// </summary>
public class SoundOrderingTests
{
    [Fact]
    public void Suggested_sounds_rank_in_the_order_they_are_listed()
    {
        Assert.Equal(0, SoundCue.Rank("Notification.Default"));
        Assert.True(SoundCue.Rank("DeviceConnect") < SoundCue.Rank(".Default"));
    }

    [Fact]
    public void Anything_not_suggested_ranks_last()
    {
        Assert.Equal(int.MaxValue, SoundCue.Rank("Notification.Looping.Alarm"));
        Assert.Equal(int.MaxValue, SoundCue.Rank("SystemHand"));
        Assert.Equal(int.MaxValue, SoundCue.Rank(null));
    }

    [Fact]
    public void Ranking_ignores_case_and_surrounding_space()
    {
        Assert.Equal(0, SoundCue.Rank("  notification.default "));
    }

    /// <summary>The case that started it: a plain sort puts Alarm 10 second.</summary>
    [Fact]
    public void Numbered_names_sort_the_way_a_person_counts()
    {
        var labels = new[] { "Alarm 10", "Alarm 2", "Alarm 1", "Alarm 21", "Alarm 3" };
        Array.Sort(labels, SoundCue.CompareLabels);

        Assert.Equal(new[] { "Alarm 1", "Alarm 2", "Alarm 3", "Alarm 10", "Alarm 21" }, labels);
    }

    [Fact]
    public void Letters_sort_normally_and_ignore_case()
    {
        var labels = new[] { "device connect", "Critical Stop", "Notification" };
        Array.Sort(labels, SoundCue.CompareLabels);

        Assert.Equal(new[] { "Critical Stop", "device connect", "Notification" }, labels);
    }

    [Fact]
    public void A_shorter_name_comes_before_the_same_name_with_more_on_the_end()
    {
        Assert.True(SoundCue.CompareLabels("Alarm", "Alarm 1") < 0);
        Assert.True(SoundCue.CompareLabels("Notification", "Notification Reminder") < 0);
    }

    [Fact]
    public void Identical_labels_compare_equal()
    {
        Assert.Equal(0, SoundCue.CompareLabels("Alarm 3", "Alarm 3"));
        Assert.Equal(0, SoundCue.CompareLabels(null, null));
        Assert.Equal(0, SoundCue.CompareLabels("Alarm 007", "Alarm 7"));
    }

    /// <summary>A digit run longer than any integer must not throw, which is why this compares as text.</summary>
    [Fact]
    public void An_absurdly_long_number_is_still_compared()
    {
        var big = new string('9', 40);
        var bigger = new string('9', 41);

        Assert.True(SoundCue.CompareLabels($"Alarm {big}", $"Alarm {bigger}") < 0);
    }

    [Fact]
    public void Sorting_is_consistent_in_both_directions()
    {
        var labels = new[] { "Alarm 10", "Notification", "Alarm 2", "device connect", "Alarm 2" };

        foreach (var left in labels)
        {
            foreach (var right in labels)
            {
                var forward = SoundCue.CompareLabels(left, right);
                var backward = SoundCue.CompareLabels(right, left);

                Assert.Equal(Math.Sign(forward), -Math.Sign(backward));
            }
        }
    }

    /// <summary>The default volume should not describe itself as loud.</summary>
    [Fact]
    public void The_default_volume_reads_as_medium()
    {
        Assert.Equal("Medium", SoundCue.DescribeVolume(SoundCue.DefaultVolume));
    }
}
