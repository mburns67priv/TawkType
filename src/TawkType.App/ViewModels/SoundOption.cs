using TawkType.Core.Settings;
using TawkType.Windows.Audio;

namespace TawkType.Desktop.ViewModels;

/// <summary>
/// One entry in a sound picker: a Windows sound event, or silence.
///
/// <see cref="ToString"/> is overridden because UI Automation and every screen reader read that
/// rather than the item template — gotcha 42, which this codebase has now got wrong four times. A
/// combo box full of <c>TawkType.Desktop.ViewModels.SoundOption</c> is not a list anybody can use
/// without looking at it, and a sound picker is the one control most likely to be used by someone
/// who is not looking at it.
/// </summary>
public sealed class SoundOption
{
    private SoundOption(string eventName, string label)
    {
        EventName = eventName;
        Label = label;
    }

    /// <summary>What gets stored. Empty means silence.</summary>
    public string EventName { get; }

    public string Label { get; }

    public bool IsSilent => SoundCue.IsSilent(EventName);

    /// <summary>The "play nothing" entry. Always offered, always first.</summary>
    public static SoundOption Silence { get; } = new(SoundCue.Silent, "No sound");

    public static SoundOption From(SchemeSound sound) => new(sound.EventName, sound.Label);

    /// <summary>
    /// An entry for a cue whose sound is not in the scheme — the user picked it, then cleared that
    /// sound in the Windows control panel, or moved the settings file from another machine. Kept in
    /// the list so opening Settings does not silently change what they chose to whatever is first.
    /// </summary>
    public static SoundOption Missing(string eventName) => new(eventName, eventName + " (not in your sound scheme)");

    public override string ToString() => Label;
}
