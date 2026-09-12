namespace TawkType.Core.Models;

/// <summary>
/// Turns the microphone's RMS into a meter position.
///
/// One unit throughout: <see cref="IAudioCapture.LevelChanged"/> raises raw RMS in 0..1, the same
/// number the samples themselves are in. It used to raise RMS multiplied by six and clamped, which
/// made every consumer's thresholds a statement about that arbitrary gain rather than about the
/// signal, and hid anything above RMS 0.167 behind the clamp.
///
/// The mapping is decibels, because that is how loudness is actually perceived and how every other
/// meter in the world is marked. Measured on real hardware: a Corsair headset at default Windows gain
/// produces an RMS of about 0.002 between words and 0.010 at the peak of ordinary speech — roughly
/// -54 to -40 dBFS. Drawn linearly from RMS, that is four pixels of a twenty-three pixel bar, which
/// is the flat waveform this was reported as.
/// </summary>
public static class AudioLevel
{
    /// <summary>Below this the meter is at rest. Quieter than a quiet room, quieter than any speech.</summary>
    public const double FloorDecibels = -60;

    /// <summary>At this the meter is full. Comfortably above normal speech, so a loud voice still has room.</summary>
    public const double CeilingDecibels = -24;

    /// <summary>
    /// Maps RMS onto a 0..1 meter position. Monotonic, and both ends are flat: silence rests and a
    /// loud voice pins rather than wrapping around.
    /// </summary>
    public static double Meter(double rms)
    {
        if (double.IsNaN(rms) || rms <= 0)
        {
            return 0;
        }

        var decibels = 20 * Math.Log10(rms);

        if (decibels <= FloorDecibels)
        {
            return 0;
        }

        return decibels >= CeilingDecibels
            ? 1
            : (decibels - FloorDecibels) / (CeilingDecibels - FloorDecibels);
    }
}
