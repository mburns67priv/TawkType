using TawkType.Core.Models;

namespace TawkType.Core.Tests;

/// <summary>
/// The meter that scales itself to whatever microphone is plugged in. The point of it is that two
/// machines an order of magnitude apart both get a usable bar, so most of these run the same
/// simulated speech at very different levels and expect the same answer.
/// </summary>
public class AdaptiveMeterTests
{
    /// <summary>Measured: a headset at default Windows gain. Quiet, and the reason this class exists.</summary>
    private const double QuietRoom = 0.0005;

    private const double QuietSpeechPeak = 0.0097;

    /// <summary>A hotter microphone, twenty decibels up. The same bar has to work for this too.</summary>
    private const double LoudRoom = 0.005;

    private const double LoudSpeechPeak = 0.1;

    /// <summary>Levels arrive about every 50 ms, so a second of audio is twenty readings.</summary>
    private static double Speak(AdaptiveMeter meter, double room, double peak, int seconds = 2)
    {
        var highest = 0.0;

        for (var reading = 0; reading < seconds * 20; reading++)
        {
            // Alternating syllables and gaps, which is roughly what speech looks like to an RMS window.
            var rms = reading % 4 < 2 ? peak : room;
            highest = Math.Max(highest, meter.Observe(rms));
        }

        return highest;
    }

    [Theory]
    [InlineData(QuietRoom, QuietSpeechPeak)]
    [InlineData(LoudRoom, LoudSpeechPeak)]
    public void Any_microphone_reaches_the_top_of_the_bar_within_a_couple_of_seconds(double room, double peak)
    {
        var meter = new AdaptiveMeter();

        Assert.True(Speak(meter, room, peak) > 0.9);
    }

    /// <summary>
    /// The regression in one test: the quiet headset and the hot microphone must end up drawing
    /// roughly the same bar, because the meter's job is to show speech rather than absolute level.
    /// </summary>
    [Fact]
    public void A_quiet_microphone_and_a_loud_one_read_alike()
    {
        var quiet = new AdaptiveMeter();
        var loud = new AdaptiveMeter();

        var quietReading = Speak(quiet, QuietRoom, QuietSpeechPeak);
        var loudReading = Speak(loud, LoudRoom, LoudSpeechPeak);

        Assert.True(Math.Abs(quietReading - loudReading) < 0.1);
    }

    [Fact]
    public void Silence_stays_at_the_bottom()
    {
        var meter = new AdaptiveMeter();

        for (var reading = 0; reading < 100; reading++)
        {
            Assert.True(meter.Observe(0) < 0.05);
        }
    }

    /// <summary>
    /// A quiet room on its own must not be amplified into a full bar. Without a minimum span, the
    /// floor and the peak converge on the noise and every rustle pins the meter.
    /// </summary>
    [Fact]
    public void A_silent_room_does_not_become_a_full_bar()
    {
        var meter = new AdaptiveMeter();
        var highest = 0.0;

        for (var reading = 0; reading < 200; reading++)
        {
            highest = Math.Max(highest, meter.Observe(QuietRoom));
        }

        Assert.True(highest < 0.5, $"room noise alone drew {highest:F2} of the bar");
    }

    [Fact]
    public void Louder_reads_higher_than_quieter_from_the_same_state()
    {
        var quieter = new AdaptiveMeter();
        var louder = new AdaptiveMeter();

        Speak(quieter, QuietRoom, QuietSpeechPeak);
        Speak(louder, QuietRoom, QuietSpeechPeak);

        Assert.True(louder.Observe(QuietSpeechPeak) > quieter.Observe(QuietSpeechPeak / 4));
    }

    /// <summary>The gaps between words have to sit visibly below the words, or the bar says nothing.</summary>
    [Fact]
    public void The_gaps_between_words_read_lower_than_the_words()
    {
        var meter = new AdaptiveMeter();
        Speak(meter, QuietRoom, QuietSpeechPeak);

        var gap = meter.Observe(QuietRoom);
        var word = meter.Observe(QuietSpeechPeak);

        Assert.True(word - gap > 0.4, $"gap {gap:F2} and word {word:F2} are too close to tell apart");
    }

    /// <summary>
    /// Someone moves from a quiet room to a noisy one. The floor has to climb, or everything reads
    /// as full from then on.
    /// </summary>
    [Fact]
    public void The_floor_follows_a_room_that_gets_noisier()
    {
        var meter = new AdaptiveMeter();
        Speak(meter, QuietRoom, QuietSpeechPeak);

        var before = meter.FloorDecibels;

        for (var reading = 0; reading < 200; reading++)
        {
            meter.Observe(LoudRoom);
        }

        Assert.True(meter.FloorDecibels > before + 10);
    }

    /// <summary>And someone who stops speaking should not leave the bar scaled to a shout for ever.</summary>
    [Fact]
    public void The_peak_decays_when_the_speaking_stops()
    {
        var meter = new AdaptiveMeter();
        Speak(meter, QuietRoom, LoudSpeechPeak);

        var loudPeak = meter.PeakDecibels;

        for (var reading = 0; reading < 100; reading++)
        {
            meter.Observe(QuietRoom);
        }

        Assert.True(meter.PeakDecibels < loudPeak - 10);
    }

    /// <summary>A new device says nothing about the old one's range.</summary>
    [Fact]
    public void Reset_forgets_the_previous_microphone()
    {
        var meter = new AdaptiveMeter();
        Speak(meter, LoudRoom, LoudSpeechPeak);

        meter.Reset();

        Assert.Equal(AdaptiveMeter.SilenceDecibels, meter.FloorDecibels);
        Assert.True(meter.Observe(QuietRoom) < 0.5);
    }

    [Fact]
    public void Nothing_escapes_the_range()
    {
        var meter = new AdaptiveMeter();

        foreach (var rms in new[] { -1.0, 0.0, double.NaN, 0.5, 1.0, 4.0 })
        {
            var position = meter.Observe(rms);
            Assert.InRange(position, 0, 1);
        }
    }
}
