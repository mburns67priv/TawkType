namespace TawkType.Core.Onboarding;

/// <summary>What the loudest moment of a few seconds of speech says about the microphone.</summary>
public enum MicrophoneVerdict
{
    /// <summary>Nothing above the noise floor. Wrong device, muted, or not plugged in.</summary>
    Silent,

    /// <summary>Something, but faint enough that a recogniser will struggle with it.</summary>
    Quiet,

    Good,

    /// <summary>Hitting the top of the range, where speech starts to clip.</summary>
    Loud,
}

/// <summary>A verdict and the sentence to put under the meter.</summary>
public readonly record struct MicrophoneReading(MicrophoneVerdict Verdict, string Message)
{
    /// <summary>
    /// Enough to carry on with. Quiet counts: plenty of laptop microphones never get past it, and
    /// refusing to continue would strand a user whose microphone works perfectly well.
    /// </summary>
    public bool Heard => Verdict != MicrophoneVerdict.Silent;
}

/// <summary>
/// Reads the peak level of a microphone test. Pure, and deliberately separate from the capture: the
/// thresholds are a judgement about speech, and a judgement is worth a test.
///
/// The levels are what <c>IAudioCapture.LevelChanged</c> raises — RMS scaled into 0..1 — so these
/// numbers move if that scaling ever does.
/// </summary>
public static class MicrophoneCheck
{
    /// <summary>Below this is room noise rather than a voice.</summary>
    public const float NoiseFloor = 0.08f;

    /// <summary>Below this it is a voice, but a distant one.</summary>
    public const float QuietSpeech = 0.2f;

    /// <summary>At this the meter is against its end stop and the waveform is being flattened.</summary>
    public const float Clipping = 0.95f;

    /// <summary>
    /// Ordered so that anything not a number — an empty test with no readings at all — falls through
    /// to Silent rather than to the loudest arm.
    /// </summary>
    public static MicrophoneReading For(float peak) => peak switch
    {
        >= QuietSpeech and <= Clipping => new(MicrophoneVerdict.Good, "That is a good level."),

        > Clipping => new(
            MicrophoneVerdict.Loud,
            "Loud enough to clip, which loses the ends of words. Try moving back, or lowering the level in Windows sound settings."),

        >= NoiseFloor => new(
            MicrophoneVerdict.Quiet,
            "Heard you, but faintly. Moving closer or raising the level in Windows sound settings will transcribe better."),

        _ => new(
            MicrophoneVerdict.Silent,
            "Nothing yet. Check the device above is the microphone you are speaking into, and that Windows has not muted it."),
    };
}
