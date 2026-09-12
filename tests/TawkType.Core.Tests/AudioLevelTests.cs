using TawkType.Core.Models;

namespace TawkType.Core.Tests;

/// <summary>
/// The mapping between the microphone and the meter. It exists because drawing RMS linearly showed a
/// working microphone as a flat line, so the numbers here are the ones measured off real hardware:
/// a headset at default Windows gain sits near RMS 0.002 between words and 0.010 at the peak of
/// ordinary speech.
/// </summary>
public class AudioLevelTests
{
    /// <summary>What a quiet room and a normal voice actually measure, on the machine this was fixed for.</summary>
    private const double RoomNoise = 0.0005;

    private const double BetweenWords = 0.002;

    private const double SpeechPeak = 0.010;

    private const double LoudVoice = 0.05;

    [Fact]
    public void Silence_rests_and_a_loud_signal_pins()
    {
        Assert.Equal(0, AudioLevel.Meter(0));
        Assert.Equal(1, AudioLevel.Meter(1));
    }

    [Fact]
    public void Nothing_escapes_the_range()
    {
        Assert.Equal(0, AudioLevel.Meter(-0.5));
        Assert.Equal(0, AudioLevel.Meter(double.NaN));
        Assert.Equal(1, AudioLevel.Meter(4));
    }

    [Fact]
    public void Louder_is_never_shown_as_quieter()
    {
        var previous = -1.0;

        for (var rms = 0.0; rms <= 1.0; rms += 0.005)
        {
            var meter = AudioLevel.Meter(rms);
            Assert.True(meter >= previous, $"{rms} went backwards");
            previous = meter;
        }
    }

    /// <summary>
    /// The bug this exists for, in pixels. A measured speech peak has to draw something anyone can
    /// see moving in a 26-pixel bar — the linear mapping drew it at 3.2px, against a 3px floor.
    /// </summary>
    [Fact]
    public void A_real_speech_peak_moves_the_meter_visibly()
    {
        var pixels = 3 + (AudioLevel.Meter(SpeechPeak) * 23);

        Assert.True(pixels >= 12, $"a measured speech peak drew {pixels:F1}px, which reads as flat");
    }

    /// <summary>
    /// And the gaps between words have to sit clearly below the peaks, or the meter is just lit up
    /// all the time and says nothing.
    /// </summary>
    [Fact]
    public void The_gaps_between_words_read_lower_than_the_words()
    {
        var gap = AudioLevel.Meter(BetweenWords);
        var peak = AudioLevel.Meter(SpeechPeak);

        Assert.True(peak - gap > 0.15, $"gap {gap:F2} and peak {peak:F2} are too close to tell apart");
    }

    [Fact]
    public void A_quiet_room_stays_near_the_floor()
        => Assert.True(AudioLevel.Meter(RoomNoise) < 0.15);

    /// <summary>A loud voice must still have somewhere to go, rather than pinning and staying there.</summary>
    [Fact]
    public void A_loud_voice_has_not_already_run_out_of_room()
    {
        var loud = AudioLevel.Meter(LoudVoice);

        Assert.True(loud > AudioLevel.Meter(SpeechPeak));
        Assert.True(loud < 1);
    }

    /// <summary>
    /// Decibels, so doubling the amplitude moves the meter the same distance wherever it happens.
    /// Only inside the mapped band: outside it the ends are deliberately flat, which is why these
    /// values sit between the floor and the ceiling rather than spanning them.
    /// </summary>
    [Fact]
    public void Equal_ratios_are_equal_distances()
    {
        var lower = AudioLevel.Meter(0.008) - AudioLevel.Meter(0.004);
        var upper = AudioLevel.Meter(0.016) - AudioLevel.Meter(0.008);

        Assert.Equal(lower, upper, 3);
    }
}
