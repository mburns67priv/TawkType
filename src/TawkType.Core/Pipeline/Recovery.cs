using TawkType.Core.Abstractions;
using TawkType.Core.Models;

namespace TawkType.Core.Pipeline;

/// <summary>What can still be done with a dictation that did not arrive.</summary>
public enum RecoveryRoute
{
    /// <summary>Nothing to recover: it was typed where it was meant to go.</summary>
    None,

    /// <summary>The text can be sent to the window it was aimed at, now that the user is asking for it.</summary>
    SendBack,

    /// <summary>
    /// Only the clipboard. Either the target is gone, or it is a window Windows will not let us type
    /// into however many times we try.
    /// </summary>
    CopyOnly,
}

/// <summary>
/// What the user is offered, in the words they see.
/// </summary>
/// <param name="Needed">Whether anything went wrong worth telling them about.</param>
/// <param name="Headline">Three or four words: what happened.</param>
/// <param name="Explanation">A sentence saying why, and what they can do.</param>
/// <param name="Route">What the buttons can actually accomplish.</param>
public sealed record RecoveryOffer(bool Needed, string Headline, string Explanation, RecoveryRoute Route)
{
    public static RecoveryOffer None { get; } = new(false, string.Empty, string.Empty, RecoveryRoute.None);
}

/// <summary>
/// Turns a finished dictation into the offer made for it.
///
/// Pure, and in Core, because the interesting part is not the window — it is deciding which of the
/// failures can still be put right and saying so honestly. Promising to send text back to a window
/// Windows will silently drop it into is worse than admitting up front that it has to be pasted by
/// hand.
/// </summary>
public static class Recovery
{
    public static RecoveryOffer For(DictationCompleted completed)
    {
        var target = completed.Target;
        var app = target?.ProcessName;
        var named = string.IsNullOrWhiteSpace(app) ? "that window" : app;

        return completed.Delivery switch
        {
            DictationDelivery.Typed or DictationDelivery.Pending => RecoveryOffer.None,

            // Elevated is the one case where trying again cannot help: Windows discards synthetic
            // input aimed at a more privileged process, and says nothing about it either time.
            DictationDelivery.CopiedToClipboard when target?.Verdict == FocusVerdict.Elevated =>
                new RecoveryOffer(
                    true,
                    "Copied instead",
                    $"{named} runs as administrator, and Windows discards typed text sent to it. "
                        + "It is on your clipboard — paste it there yourself.",
                    RecoveryRoute.CopyOnly),

            DictationDelivery.CopiedToClipboard => new RecoveryOffer(
                true,
                "Copied instead",
                CanSendTo(target)
                    ? $"There was nowhere to type it in {named}, so it went to your clipboard. "
                        + "You can send it back, or copy it somewhere else."
                    : $"There was nowhere to type it in {named}, so it went to your clipboard.",
                CanSendTo(target) ? RecoveryRoute.SendBack : RecoveryRoute.CopyOnly),

            DictationDelivery.Failed => new RecoveryOffer(
                true,
                "Not delivered",
                CanSendTo(target)
                    ? $"The text could not be typed into {named}. It is still here — try again, or copy it."
                    : "The text could not be typed. It is still here to copy.",
                CanSendTo(target) ? RecoveryRoute.SendBack : RecoveryRoute.CopyOnly),

            _ => RecoveryOffer.None,
        };
    }

    /// <summary>A window we can name and an identity we can still find it by, and not an elevated one.</summary>
    private static bool CanSendTo(FocusTarget? target)
        => target is { Window: not null } && target.Verdict != FocusVerdict.Elevated;
}
