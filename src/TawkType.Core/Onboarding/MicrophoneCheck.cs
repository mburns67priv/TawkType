namespace TawkType.Core.Onboarding;

/// <summary>What the loudest moment of a few seconds of speech says about the microphone.</summary>
public enum MicrophoneVerdict
{
    /// <summary>Nothing above a quiet room. Wrong device, muted, or not plugged in.</summary>
    Silent,

    /// <summary>Quieter than most, and perfectly usable. Not a fault.</summary>
    Quiet,

    Good,

    /// <summary>Hitting the top of the range, where speech starts to clip.</summary>
    Loud,
}

/// <summary>A verdict and the sentence to put under the meter.</summary>
public readonly record struct MicrophoneReading(MicrophoneVerdict Verdict, string Message)
{
    /// <summary>
    /// Enough to carry on with. Everything except silence counts: the question this answers is
    /// "can TawkType hear you", and a quiet signal that transcribes perfectly well is a yes.
    /// </summary>
    public bool Heard => Verdict != MicrophoneVerdict.Silent;
}

/// <summary>
/// Reads the peak RMS of a microphone test. Pure, and deliberately separate from the capture: these
/// thresholds are a judgement about speech, and a judgement is worth a test.
///
/// The numbers are measured rather than assumed. The first version was written against a comment
/// claiming speech RMS sits around 0.02–0.2; the first person to run TawkType on real hardware was
/// told his working headset was silent, because it peaks at 0.0097 — below a floor of 0.0133. These
/// thresholds come from that measurement, and `tools/TawkType.Mic` is how to take another one.
///
/// The bar being answered is transcribability, not recording quality. Both engines normalise their
/// input, so audio far below what an engineer would accept still dictates perfectly.
/// </summary>
public static class MicrophoneCheck
{
    /// <summary>
    /// Below this is a quiet room rather than a voice — about -56 dBFS. A measured working headset
    /// peaks six times higher than this, which is the margin the old floor did not have.
    /// </summary>
    public const float NoiseFloor = 0.0015f;

    /// <summary>Below this it is a voice on the quiet side — usable, worth a word about.</summary>
    public const float QuietSpeech = 0.008f;

    /// <summary>At this the signal is hot enough that peaks will start flattening.</summary>
    public const float Clipping = 0.15f;

    /// <summary>
    /// Judges the loudest RMS window of the test. Ordered so that anything not a number — an empty
    /// test with no readings at all — falls through to Silent rather than to the loudest arm.
    /// </summary>
    public static MicrophoneReading For(float peakRms) => peakRms switch
    {
        >= QuietSpeech and <= Clipping => new(MicrophoneVerdict.Good, "That is a good level."),

        > Clipping => new(
            MicrophoneVerdict.Loud,
            "Loud enough to clip, which loses the ends of words. Try moving back, or lowering the level in Windows sound settings."),

        // Deliberately not phrased as a problem. It is a normal level for plenty of hardware, it
        // transcribes fine, and the person reading it knows their microphone works.
        >= NoiseFloor => new(
            MicrophoneVerdict.Quiet,
            "Heard you. Your microphone is on the quiet side, which transcribes fine — raising its level in Windows sound settings buys a little accuracy if you want it."),

        _ => new(
            MicrophoneVerdict.Silent,
            "Nothing yet. Check the device above is the microphone you are speaking into, and that Windows has not muted it."),
    };
}
