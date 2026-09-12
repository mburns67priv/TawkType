namespace TawkType.Core.Models;

/// <summary>
/// Turns a raw input level into one a meter can show.
///
/// The raw value is RMS, and RMS of speech is small and lives at the bottom of its range: a quiet but
/// perfectly transcribable microphone sits around 0.005–0.02 before scaling. Drawn linearly into a
/// 23-pixel bar that is four pixels of movement, which reads as a dead meter — reported from a real
/// install, where dictation worked and the waveform never moved.
///
/// A square root spends the range where the signal actually is. It is the same reason audio meters
/// are marked in decibels rather than in amplitude.
/// </summary>
public static class AudioLevel
{
    /// <summary>
    /// Maps a 0..1 input level onto a 0..1 meter position. Monotonic, so a louder sound is never
    /// shown as quieter, and the ends are left alone: silence reads as silence and a clipped signal
    /// still reads as full.
    /// </summary>
    public static double Perceptual(double level)
    {
        if (double.IsNaN(level) || level <= 0)
        {
            return 0;
        }

        return level >= 1 ? 1 : Math.Sqrt(level);
    }
}
