namespace TawkType.Core.Settings;

/// <summary>
/// Which sound plays when a dictation starts, finishes and fails, and how loud.
///
/// A cue is stored as a **Windows sound event name** — <c>Notification.Default</c>,
/// <c>DeviceConnect</c>, <c>.Default</c> — not as a path and not as an enum of our own. The name is
/// what Windows keys its own sound scheme on, so a user who changes their scheme, or moves the
/// settings file to another machine, keeps the sound they meant rather than a path that has gone.
/// An empty string is silence, and is a real choice: somebody may want to hear the start of a
/// dictation and nothing else.
///
/// The five sounds .NET exposes through <c>SystemSounds</c> are all alerts, which is why the first
/// version of this sounded like Windows telling you off. The scheme holds far more than five.
/// </summary>
public sealed class SoundSettings
{
    /// <summary>The cue meaning "I am listening". Silence when empty.</summary>
    public string Start { get; set; } = SoundCue.DefaultStart;

    /// <summary>The cue meaning "I have stopped listening and I am working on it".</summary>
    public string Stop { get; set; } = SoundCue.DefaultStop;

    /// <summary>The cue meaning "that did not work". An error may reasonably still sound like one.</summary>
    public string Error { get; set; } = SoundCue.DefaultError;

    /// <summary>0 to 100. Applied to the sound TawkType plays, not to anything else on the machine.</summary>
    public int Volume { get; set; } = SoundCue.DefaultVolume;

    public SoundSettings Clone() => new()
    {
        Start = Start,
        Stop = Stop,
        Error = Error,
        Volume = Volume,
    };
}
