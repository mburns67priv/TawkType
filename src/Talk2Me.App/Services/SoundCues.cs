using System.Media;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Models;

namespace Talk2Me.Desktop.Services;

/// <summary>
/// Optional short sounds for the start and end of a dictation, and for a failure.
///
/// Windows' own system sounds rather than shipped audio files: they respect the user's sound scheme,
/// they are silent for anyone who has chosen silence, and they need no assets. Off by default —
/// a sound on every dictation is a lot of sound — and worth having for anyone who cannot watch the
/// bar while they speak.
/// </summary>
public sealed class SoundCues
{
    private readonly ISettingsProvider _settings;

    public SoundCues(ISettingsProvider settings)
    {
        _settings = settings;
    }

    public void Started() => Play(SystemSounds.Asterisk);

    public void Finished() => Play(SystemSounds.Beep);

    public void Failed() => Play(SystemSounds.Hand);

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

    private void Play(SystemSound sound)
    {
        if (!_settings.Current.PlaySounds)
        {
            return;
        }

        try
        {
            sound.Play();
        }
        catch (Exception)
        {
            // No audio device, or a sound scheme that has been taken apart. Never worth interrupting
            // a dictation over.
        }
    }
}
