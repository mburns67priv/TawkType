using TawkType.Core.Abstractions;
using TawkType.Core.Models;
using TawkType.Core.Settings;
using TawkType.Windows.Audio;

namespace TawkType.Desktop.Services;

/// <summary>
/// Optional short sounds for the start and end of a dictation, and for a failure.
///
/// Still the user's own Windows sounds rather than audio shipped in the package: they respect the
/// scheme, they need no assets, and nothing has to be licensed. What changed is which ones are
/// offered and how they are played.
///
/// This used to call <c>SystemSounds.Asterisk</c>, <c>Beep</c> and <c>Hand</c> — the only sounds
/// .NET exposes, all three of them alerts, and <c>Hand</c> is literally Critical Stop. Reported as
/// "the default sounds like a Windows error", which it was. Each cue is a choice now, from the whole
/// scheme, and the defaults lead with a notification chime rather than an alarm.
///
/// <see cref="TawkTypeSettings.PlaySounds"/> remains the master switch and is still off by default.
/// </summary>
public sealed class SoundCues : IDisposable
{
    private readonly ISettingsProvider _settings;
    private readonly CuePlayer _player = new();

    public SoundCues(ISettingsProvider settings)
    {
        _settings = settings;
    }

    public void Started() => Play(Sounds.Start);

    public void Finished() => Play(Sounds.Stop);

    public void Failed() => Play(Sounds.Error);

    /// <summary>Plays the cue for a state change, or nothing when that state has no sound of its own.</summary>
    public void ForState(DictationState state)
    {
        switch (state)
        {
            case DictationState.Listening:
                Started();
                break;
            case DictationState.Transcribing:
                Finished();
                break;
            case DictationState.Error:
                Failed();
                break;
        }
    }

    /// <summary>
    /// Plays a cue at a given volume whatever the settings say, for the Preview buttons.
    ///
    /// Deliberately ignores the master switch: somebody auditioning sounds in Settings has asked to
    /// hear this one, and the checkbox they may not have ticked yet is about dictations.
    /// </summary>
    public void Preview(string? cue, int volume)
        => _player.Play(WindowsSoundScheme.PathFor(SoundCue.Normalise(cue)), SoundCue.Gain(volume));

    public void Dispose() => _player.Dispose();

    private SoundSettings Sounds => _settings.Current.Sounds;

    private void Play(string? cue)
    {
        if (!_settings.Current.PlaySounds || SoundCue.IsSilent(cue))
        {
            return;
        }

        _player.Play(WindowsSoundScheme.PathFor(cue), SoundCue.Gain(Sounds.Volume));
    }
}
