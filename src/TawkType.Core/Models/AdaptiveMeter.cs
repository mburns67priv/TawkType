namespace TawkType.Core.Models;

/// <summary>
/// Scales a level meter to whatever microphone is actually plugged in.
///
/// Fixed decibel limits cannot serve everyone. A headset at default Windows gain peaks near
/// -40 dBFS; a condenser on an interface can peak twenty decibels higher, and the same bar has to
/// read sensibly for both. Tuning the constants to one machine is what produced a flat waveform and a
/// working microphone reported as silent.
///
/// So the meter watches its own signal instead. It keeps two estimates in decibels — a noise floor
/// that drops instantly and recovers slowly, and a peak that rises instantly and decays slowly — and
/// reports where the current level sits between them. Within a second or two of speech, any
/// microphone uses the whole bar, and changing headset or moving to a noisier room re-adapts without
/// anybody being asked to do anything.
///
/// This is only for *drawing*. Whether a signal is loud enough to be a voice at all is an absolute
/// question about the signal, and <see cref="TawkType.Core.Onboarding.MicrophoneCheck"/> keeps
/// answering it in absolute terms.
/// </summary>
public sealed class AdaptiveMeter
{
    /// <summary>Anything below this is treated as digital silence rather than a very quiet room.</summary>
    public const double SilenceDecibels = -80;

    /// <summary>
    /// The smallest range the meter will scale to. Without it, a moment of near-silence would put the
    /// floor and the peak on top of each other and every tiny noise would slam the bar to full.
    /// </summary>
    public const double MinimumSpanDecibels = 18;

    /// <summary>
    /// How fast the remembered peak falls, per second, once the speaking stops. Slow enough that the
    /// gaps between words do not rescale the bar, fast enough to follow someone moving closer.
    /// </summary>
    public const double PeakDecayPerSecond = 12;

    /// <summary>
    /// How fast the remembered floor climbs, per second, when the room gets noisier. Slower than the
    /// peak decays, because mistaking speech for room noise is the worse error.
    /// </summary>
    public const double FloorRisePerSecond = 3;

    /// <summary>Levels arrive about this often, and the rates above are expressed per second.</summary>
    private const double AssumedIntervalSeconds = 0.05;

    private double _floor = SilenceDecibels;
    private double _peak = SilenceDecibels + MinimumSpanDecibels;

    /// <summary>
    /// Whether anything has been heard yet. Until it has, there is nothing to scale to, and starting
    /// from an assumed floor of digital silence would draw the first quiet moment of a quiet room at
    /// three quarters of the bar before settling.
    /// </summary>
    private bool _seeded;

    /// <summary>Where the meter currently thinks the quiet end is, in dBFS. For diagnostics.</summary>
    public double FloorDecibels => _floor;

    /// <summary>And the loud end. For diagnostics.</summary>
    public double PeakDecibels => _peak;

    /// <summary>
    /// Forgets what it has learned. Called when the capture starts or the device changes: the
    /// previous microphone's range says nothing about this one's.
    /// </summary>
    public void Reset()
    {
        _floor = SilenceDecibels;
        _peak = SilenceDecibels + MinimumSpanDecibels;
        _seeded = false;
    }

    /// <summary>
    /// Takes one RMS reading and returns where it sits on the meter, 0..1.
    /// </summary>
    public double Observe(double rms)
    {
        var decibels = ToDecibels(rms);

        // The first reading is the only information there is, so it becomes both ends of the range.
        // Everything after it moves them.
        if (!_seeded)
        {
            _seeded = true;
            _floor = decibels;
            _peak = decibels + MinimumSpanDecibels;
            return 0;
        }

        // The floor drops to meet a new quiet immediately, and creeps back up when the room is
        // noisier than it was. Dropping fast matters: the first quiet moment after a loud one is what
        // tells the meter where the bottom is.
        _floor = decibels < _floor
            ? decibels
            : Math.Min(_floor + (FloorRisePerSecond * AssumedIntervalSeconds), decibels);

        // The peak is the mirror image: instant attack so a syllable registers on its first reading,
        // slow release so the bar does not rescale between words.
        _peak = decibels > _peak
            ? decibels
            : Math.Max(_peak - (PeakDecayPerSecond * AssumedIntervalSeconds), _floor + MinimumSpanDecibels);

        if (_peak - _floor < MinimumSpanDecibels)
        {
            _peak = _floor + MinimumSpanDecibels;
        }

        var position = (decibels - _floor) / (_peak - _floor);
        return Math.Clamp(position, 0, 1);
    }

    private static double ToDecibels(double rms)
    {
        if (double.IsNaN(rms) || rms <= 0)
        {
            return SilenceDecibels;
        }

        return Math.Max(SilenceDecibels, 20 * Math.Log10(Math.Min(rms, 1)));
    }
}
