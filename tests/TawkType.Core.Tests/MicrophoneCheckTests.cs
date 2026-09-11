using TawkType.Core.Onboarding;

namespace TawkType.Core.Tests;

/// <summary>
/// The judgement first run makes about a microphone from the loudest moment of a short test. These
/// thresholds are the difference between "TawkType can hear you" and a user who gets to the end of
/// setup before discovering the wrong device was selected all along.
/// </summary>
public class MicrophoneCheckTests
{
    [Fact]
    public void Silence_is_not_a_microphone_working()
    {
        var reading = MicrophoneCheck.For(0f);

        Assert.Equal(MicrophoneVerdict.Silent, reading.Verdict);
        Assert.False(reading.Heard);
        Assert.Contains("muted", reading.Message);
    }

    /// <summary>
    /// A faint microphone is still a microphone. Plenty of laptops never get past this, and refusing
    /// to continue would strand a user whose setup does in fact work.
    /// </summary>
    [Fact]
    public void A_quiet_microphone_is_heard_but_advised_about()
    {
        var reading = MicrophoneCheck.For(0.12f);

        Assert.Equal(MicrophoneVerdict.Quiet, reading.Verdict);
        Assert.True(reading.Heard);
    }

    [Fact]
    public void A_normal_speaking_level_is_good()
    {
        var reading = MicrophoneCheck.For(0.5f);

        Assert.Equal(MicrophoneVerdict.Good, reading.Verdict);
        Assert.True(reading.Heard);
    }

    [Fact]
    public void A_level_against_the_end_stop_is_clipping()
    {
        var reading = MicrophoneCheck.For(1f);

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
        Assert.Equal(MicrophoneVerdict.Silent, MicrophoneCheck.For(MicrophoneCheck.NoiseFloor - 0.001f).Verdict);
        Assert.Equal(MicrophoneVerdict.Quiet, MicrophoneCheck.For(MicrophoneCheck.NoiseFloor).Verdict);
        Assert.Equal(MicrophoneVerdict.Quiet, MicrophoneCheck.For(MicrophoneCheck.QuietSpeech - 0.001f).Verdict);
        Assert.Equal(MicrophoneVerdict.Good, MicrophoneCheck.For(MicrophoneCheck.QuietSpeech).Verdict);
        Assert.Equal(MicrophoneVerdict.Good, MicrophoneCheck.For(MicrophoneCheck.Clipping).Verdict);
        Assert.Equal(MicrophoneVerdict.Loud, MicrophoneCheck.For(MicrophoneCheck.Clipping + 0.001f).Verdict);
    }
}
