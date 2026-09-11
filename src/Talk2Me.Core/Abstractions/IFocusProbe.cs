using Talk2Me.Core.Text;

namespace Talk2Me.Core.Abstractions;

public enum FocusVerdict
{
    /// <summary>Nothing conclusive. Treated as typeable — see <see cref="FocusTarget.CanType"/>.</summary>
    Unknown,

    /// <summary>The focused element is an editable text field.</summary>
    Editable,

    /// <summary>The focused element is definitely not somewhere text can go.</summary>
    NotEditable,

    /// <summary>
    /// The focused window belongs to a more privileged process. Synthetic input to it is dropped by
    /// Windows without any error, so typing would silently go nowhere.
    /// </summary>
    Elevated,
}

/// <summary>What was focused when the user started speaking.</summary>
/// <param name="Verdict">Whether text can be typed there.</param>
/// <param name="ProcessName">The owning process, for the log and for explaining what happened.</param>
/// <param name="Description">Control type or role, for the log. Free-form.</param>
/// <param name="Window">
/// An opaque identifier for the window that was in front, or null when it could not be determined.
/// Compared before delivery so text is not typed into a window the user has since moved away from.
/// It identifies a window and nothing finer: two fields in the same form, or two tabs in the same
/// browser, are the same window and cannot be told apart this way.
/// </param>
public sealed record FocusTarget(
    FocusVerdict Verdict,
    string? ProcessName = null,
    string? Description = null,
    long? Window = null)
{
    public static FocusTarget Unknown { get; } = new(FocusVerdict.Unknown);

    /// <summary>
    /// Deliberately permissive: only a confident negative stops the text being typed. Accessibility
    /// information is patchy — some Java apps, games and custom-drawn editors expose nothing useful —
    /// and refusing to type into a field that would have worked is a worse bug than the one this
    /// check exists to fix.
    /// </summary>
    public bool CanType => Verdict is FocusVerdict.Editable or FocusVerdict.Unknown;

    /// <summary>A short sentence for the overlay explaining why the text was not typed.</summary>
    public string Explain() => Verdict switch
    {
        FocusVerdict.Elevated => ProcessName is null
            ? "That window runs as administrator"
            : $"{ProcessName} runs as administrator",
        FocusVerdict.NotEditable => "No text field focused",
        _ => string.Empty,
    };
}

/// <summary>
/// Reports what currently has keyboard focus. Called when the push-to-talk key goes down, so it has the
/// whole utterance to answer in — but it must never throw, and must time out rather than hang on an
/// unresponsive application.
/// </summary>
public interface IFocusProbe
{
    FocusTarget Probe(CancellationToken cancellationToken = default);

    /// <summary>
    /// Which window is in front right now, cheaply — no accessibility tree, no cross-process calls that
    /// can block. Used to check the target has not changed while transcription was running. Null when
    /// the platform cannot say.
    /// </summary>
    long? CurrentWindow() => null;

    /// <summary>
    /// The text either side of the caret, read immediately before delivery — the caret moves while a
    /// dictation is being transcribed, so this cannot be answered at key-down like the rest of the
    /// probe.
    ///
    /// Best-effort by contract: it must never throw, must respect the token, and returns
    /// <see cref="CaretContext.Unknown"/> for everything it cannot answer. The default does exactly
    /// that, so a platform or a test double that does not implement it costs nothing.
    /// </summary>
    CaretContext ReadCaret(CancellationToken cancellationToken = default) => CaretContext.Unknown;
}
