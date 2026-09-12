using TawkType.Core.Models;

namespace TawkType.Core.Tests;

/// <summary>
/// The curve between the microphone and the meter. It exists because the linear version showed a
/// working microphone as a flat line, so the test that matters is the quiet one.
/// </summary>
public class AudioLevelTests
{
    [Fact]
    public void Silence_is_silence_and_full_is_full()
    {
        Assert.Equal(0, AudioLevel.Perceptual(0));
        Assert.Equal(1, AudioLevel.Perceptual(1));
    }

    [Fact]
    public void Nothing_escapes_the_range()
    {
        Assert.Equal(0, AudioLevel.Perceptual(-0.5));
        Assert.Equal(1, AudioLevel.Perceptual(4));
        Assert.Equal(0, AudioLevel.Perceptual(double.NaN));
    }

    [Fact]
    public void Louder_is_never_shown_as_quieter()
    {
        var previous = -1.0;

        for (var level = 0.0; level <= 1.0; level += 0.01)
        {
            var height = AudioLevel.Perceptual(level);
            Assert.True(height >= previous, $"{level} went backwards");
            previous = height;
        }
    }

    /// <summary>
    /// The bug this exists for. A quiet microphone — the raw level around 0.05 after scaling — has to
    /// produce visible movement, not a fifth of one pixel in a 23-pixel bar.
    /// </summary>
    [Theory]
    [InlineData(0.02)]
    [InlineData(0.05)]
    [InlineData(0.12)]
    public void A_quiet_voice_still_moves_the_meter(double level)
    {
        var pixels = 3 + (AudioLevel.Perceptual(level) * 23);

        Assert.True(pixels >= 6, $"{level} drew {pixels:F1}px, which nobody can see moving");
    }

    /// <summary>
    /// And it has to be an improvement rather than a different flat line: the curve must lift a quiet
    /// signal well clear of where the linear mapping left it.
    /// </summary>
    [Fact]
    public void The_curve_lifts_quiet_speech_clear_of_the_linear_mapping()
    {
        const double quiet = 0.05;

        Assert.True(AudioLevel.Perceptual(quiet) > quiet * 3);
    }

    /// <summary>A loud signal must still have somewhere to go, or the meter pins and stops meaning anything.</summary>
    [Fact]
    public void A_loud_voice_has_not_already_run_out_of_room()
        => Assert.True(AudioLevel.Perceptual(0.5) < 0.8);
}
