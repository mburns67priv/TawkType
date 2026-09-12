using TawkType.Core.Onboarding;

namespace TawkType.Core.Tests;

/// <summary>
/// The judgement first run makes about a microphone from the loudest RMS window of a short test.
///
/// The numbers are measured, not assumed. The first version of these thresholds was written against
/// a comment claiming speech RMS sits around 0.02–0.2, and told the owner of a working Corsair headset
/// that his microphone was silent: it peaks at 0.0097. That measurement is the fixture below.
/// </summary>
public class MicrophoneCheckTests
{
    /// <summary>Measured: the loudest window of ten seconds of ordinary speech, Corsair headset, default gain.</summary>
    private const float MeasuredHeadsetPeak = 0.0097f;

    /// <summary>Measured on the same machine: the median window, which is mostly the gaps between words.</summary>
    private const float MeasuredHeadsetMedian = 0.0023f;

    /// <summary>
    /// The regression this file exists for. A microphone that people hear fine and that transcribes
    /// fine must not be called silent.
    /// </summary>
    [Fact]
    public void A_working_headset_is_heard()
    {
        var reading = MicrophoneCheck.For(MeasuredHeadsetPeak);

        Assert.True(reading.Heard);
        Assert.NotEqual(MicrophoneVerdict.Silent, reading.Verdict);
    }

    /// <summary>
    /// And it must not be told it has a problem either. Everything below clipping transcribes; the
    /// difference between quiet and good is worth a word, not a warning.
    /// </summary>
    [Fact]
    public void A_quiet_microphone_is_not_described_as_a_fault()
    {
        var reading = MicrophoneCheck.For(0.003f);

        Assert.Equal(MicrophoneVerdict.Quiet, reading.Verdict);
        Assert.True(reading.Heard);
        Assert.Contains("Heard you", reading.Message);
        Assert.Contains("transcribes fine", reading.Message);
    }

    [Fact]
    public void Silence_is_not_a_microphone_working()
    {
        var reading = MicrophoneCheck.For(0f);

        Assert.Equal(MicrophoneVerdict.Silent, reading.Verdict);
        Assert.False(reading.Heard);
        Assert.Contains("muted", reading.Message);
    }

    /// <summary>
    /// The floor has to sit below a real voice with room to spare, which is exactly what the old one
    /// did not do — it was above this measurement.
    /// </summary>
    [Fact]
    public void The_noise_floor_is_well_below_measured_speech()
        => Assert.True(
            MicrophoneCheck.NoiseFloor * 4 < MeasuredHeadsetPeak,
            $"floor {MicrophoneCheck.NoiseFloor} leaves no margin under a measured peak of {MeasuredHeadsetPeak}");

    /// <summary>
    /// The gaps between words are not speech. If the floor crept above them the meter would still
    /// work, but a test that never sees a word would claim it had.
    /// </summary>
    [Fact]
    public void The_floor_sits_below_the_gaps_between_words_too()
        => Assert.True(MicrophoneCheck.NoiseFloor < MeasuredHeadsetMedian);

    [Fact]
    public void A_normal_level_is_good()
        => Assert.Equal(MicrophoneVerdict.Good, MicrophoneCheck.For(0.03f).Verdict);

    [Fact]
    public void A_hot_signal_is_clipping()
    {
        var reading = MicrophoneCheck.For(0.4f);

        Assert.Equal(MicrophoneVerdict.Loud, reading.Verdict);
        Assert.True(reading.Heard);
        Assert.Contains("clip", reading.Message);
    }

    /// <summary>A test with no readings at all reads as silence, not as the loudest arm.</summary>
    [Fact]
    public void A_peak_that_is_not_a_number_is_silence()
        => Assert.Equal(MicrophoneVerdict.Silent, MicrophoneCheck.For(float.NaN).Verdict);

    [Fact]
    public void The_verdicts_step_up_in_order()
    {
        Assert.Equal(MicrophoneVerdict.Silent, MicrophoneCheck.For(MicrophoneCheck.NoiseFloor - 0.0001f).Verdict);
        Assert.Equal(MicrophoneVerdict.Quiet, MicrophoneCheck.For(MicrophoneCheck.NoiseFloor).Verdict);
        Assert.Equal(MicrophoneVerdict.Quiet, MicrophoneCheck.For(MicrophoneCheck.QuietSpeech - 0.0001f).Verdict);
        Assert.Equal(MicrophoneVerdict.Good, MicrophoneCheck.For(MicrophoneCheck.QuietSpeech).Verdict);
        Assert.Equal(MicrophoneVerdict.Good, MicrophoneCheck.For(MicrophoneCheck.Clipping).Verdict);
        Assert.Equal(MicrophoneVerdict.Loud, MicrophoneCheck.For(MicrophoneCheck.Clipping + 0.0001f).Verdict);
    }
}
