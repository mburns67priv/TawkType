namespace TawkType.Core.Abstractions;

/// <summary>
/// Global push-to-talk key. Raises <see cref="Pressed"/> once on key-down (auto-repeat filtered)
/// and <see cref="Released"/> once on key-up, regardless of which application has focus.
/// </summary>
public interface IPushToTalkHotkey : IDisposable
{
    event EventHandler? Pressed;

    event EventHandler? Released;

    /// <summary>
    /// The user asked to abandon what is in progress — Escape, while a dictation is running. Only
    /// raised while <see cref="DictationInProgress"/> is set, so Escape belongs to the focused
    /// application the rest of the time.
    /// </summary>
    event EventHandler? CancelRequested;

    /// <summary>
    /// The user asked for the next dictation mode. Raised whether or not a dictation is running; the
    /// reducer already declines to fire it during one.
    /// </summary>
    event EventHandler? NextModeRequested;

    /// <summary>
    /// Set by the engine while there is something to cancel. It is the hotkey layer that sees Escape
    /// first, and it has no other way to know whether taking it would be stealing.
    /// </summary>
    bool DictationInProgress { get; set; }

    /// <summary>Begins listening. On Windows this must be called from a thread that pumps messages (the UI thread).</summary>
    void Start();

    void Stop();
}
