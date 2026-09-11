using TawkType.Core.Abstractions;
using TawkType.Core.Models;
using TawkType.Core.Pipeline;

namespace TawkType.Core.Tests;

public class RecoveryTests
{
    private static DictationCompleted Completed(DictationDelivery delivery, FocusTarget? target = null)
        => new("raw", "clean", TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(300))
        {
            Delivery = delivery,
            Target = target,
        };

    [Theory]
    [InlineData(DictationDelivery.Typed)]
    [InlineData(DictationDelivery.Pending)]
    public void A_dictation_that_arrived_needs_no_recovery(DictationDelivery delivery)
        => Assert.False(Recovery.For(Completed(delivery)).Needed);

    [Fact]
    public void A_copied_dictation_can_be_sent_back_to_the_window_it_was_aimed_at()
    {
        var target = new FocusTarget(FocusVerdict.NotEditable, "chrome", "document", Window: 42);

        var offer = Recovery.For(Completed(DictationDelivery.CopiedToClipboard, target));

        Assert.True(offer.Needed);
        Assert.Equal(RecoveryRoute.SendBack, offer.Route);
        Assert.Contains("chrome", offer.Explanation);
    }

    /// <summary>
    /// Windows discards synthetic input aimed at a more privileged process, so offering to send it
    /// again would fail exactly as silently as the first attempt did.
    /// </summary>
    [Fact]
    public void An_elevated_window_is_never_offered_a_second_attempt()
    {
        var target = new FocusTarget(FocusVerdict.Elevated, "regedit", "elevated window", Window: 42);

        var offer = Recovery.For(Completed(DictationDelivery.CopiedToClipboard, target));

        Assert.Equal(RecoveryRoute.CopyOnly, offer.Route);
        Assert.Contains("administrator", offer.Explanation);
    }

    /// <summary>Nothing to aim at means nothing to promise.</summary>
    [Fact]
    public void A_target_we_cannot_find_again_is_copy_only()
    {
        var target = new FocusTarget(FocusVerdict.NotEditable, "chrome", "document", Window: null);

        Assert.Equal(RecoveryRoute.CopyOnly, Recovery.For(Completed(DictationDelivery.CopiedToClipboard, target)).Route);
    }

    [Fact]
    public void A_failed_delivery_is_offered_another_attempt()
    {
        var target = new FocusTarget(FocusVerdict.Editable, "notepad", "edit", Window: 7);

        var offer = Recovery.For(Completed(DictationDelivery.Failed, target));

        Assert.True(offer.Needed);
        Assert.Equal(RecoveryRoute.SendBack, offer.Route);
        Assert.Equal("Not delivered", offer.Headline);
    }

    /// <summary>The explanation is shown on its own, so it has to read without the window around it.</summary>
    [Fact]
    public void An_unnamed_window_still_reads_as_a_sentence()
    {
        var offer = Recovery.For(Completed(DictationDelivery.Failed));

        Assert.DoesNotContain("  ", offer.Explanation);
        Assert.EndsWith(".", offer.Explanation);
        Assert.DoesNotContain("null", offer.Explanation);
    }
}
