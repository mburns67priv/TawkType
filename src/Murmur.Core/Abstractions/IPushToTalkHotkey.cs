namespace Murmur.Core.Abstractions;

/// <summary>
/// Global push-to-talk key. Raises <see cref="Pressed"/> once on key-down (auto-repeat filtered)
/// and <see cref="Released"/> once on key-up, regardless of which application has focus.
/// </summary>
public interface IPushToTalkHotkey : IDisposable
{
    event EventHandler? Pressed;

    event EventHandler? Released;

    /// <summary>Begins listening. On Windows this must be called from a thread that pumps messages (the UI thread).</summary>
    void Start();

    void Stop();
}
