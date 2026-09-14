using TawkType.Core.Settings;

namespace TawkType.Core.Tests;

public class SoundCueTests
{
    [Fact]
    public void An_empty_cue_is_silence()
    {
        Assert.True(SoundCue.IsSilent(null));
        Assert.True(SoundCue.IsSilent(string.Empty));
        Assert.True(SoundCue.IsSilent("   "));
        Assert.False(SoundCue.IsSilent("Notification.Default"));
    }

    [Fact]
    public void Normalising_trims_a_name_and_flattens_blanks_to_silence()
    {
        Assert.Equal("Notification.Default", SoundCue.Normalise("  Notification.Default "));
        Assert.Equal(SoundCue.Silent, SoundCue.Normalise("   "));
        Assert.Equal(SoundCue.Silent, SoundCue.Normalise(null));
    }

    [Theory]
    [InlineData(-40, 0)]
    [InlineData(0, 0)]
    [InlineData(70, 70)]
    [InlineData(100, 100)]
    [InlineData(1000, 100)]
    public void Volume_is_clamped_to_the_slider(int given, int expected)
    {
        Assert.Equal(expected, SoundCue.ClampVolume(given));
    }

    [Fact]
    public void Silence_is_silence_and_full_is_unchanged()
    {
        Assert.Equal(0f, SoundCue.Gain(0));
        Assert.Equal(1f, SoundCue.Gain(100));
    }

    /// <summary>
    /// The reason the curve is squared: halfway along the slider should be audibly halfway, and a
    /// linear gain puts it at four fifths of full amplitude, which sounds nearly as loud.
    /// </summary>
    [Fact]
    public void Halfway_along_the_slider_is_a_quarter_of_the_amplitude()
    {
        Assert.Equal(0.25f, SoundCue.Gain(50), 3);
    }

    [Fact]
    public void Gain_never_leaves_the_usable_range()
    {
        foreach (var volume in new[] { -100, -1, 0, 1, 50, 99, 100, 101, 5000 })
        {
            var gain = SoundCue.Gain(volume);
            Assert.InRange(gain, 0f, 1f);
        }
    }

    [Fact]
    public void Gain_climbs_with_the_slider()
    {
        var previous = -1f;
        for (var volume = 0; volume <= 100; volume += 5)
        {
            var gain = SoundCue.Gain(volume);
            Assert.True(gain > previous, $"volume {volume} was not louder than the step below it");
            previous = gain;
        }
    }

    [Theory]
    [InlineData(0, "Muted")]
    [InlineData(20, "Quiet")]
    [InlineData(50, "Medium")]
    [InlineData(80, "Loud")]
    [InlineData(100, "Full")]
    public void The_slider_describes_itself_in_words(int volume, string expected)
    {
        Assert.Equal(expected, SoundCue.DescribeVolume(volume));
    }

    /// <summary>
    /// The defaults are the answer to "it sounds like a Windows error". Start is a notification
    /// chime, stop is silent, and only a failure gets an alert.
    /// </summary>
    [Fact]
    public void The_defaults_do_not_lead_with_an_alert()
    {
        var settings = new SoundSettings();

        Assert.Equal("Notification.Default", settings.Start);
        Assert.True(SoundCue.IsSilent(settings.Stop));
        Assert.Equal("SystemHand", settings.Error);
        Assert.Equal(70, settings.Volume);
    }

    [Fact]
    public void A_clone_carries_every_choice_and_shares_nothing()
    {
        var original = new SoundSettings { Start = "DeviceConnect", Stop = "DeviceDisconnect", Error = SoundCue.Silent, Volume = 35 };
        var copy = original.Clone();

        Assert.Equal("DeviceConnect", copy.Start);
        Assert.Equal("DeviceDisconnect", copy.Stop);
        Assert.Equal(SoundCue.Silent, copy.Error);
        Assert.Equal(35, copy.Volume);

        copy.Start = "MailBeep";
        Assert.Equal("DeviceConnect", original.Start);
    }

    /// <summary>Gotcha 11: a nested section that Clone forgets is edited in place by the Settings window.</summary>
    [Fact]
    public void Cloning_the_whole_settings_does_not_share_the_sound_section()
    {
        var settings = new TawkTypeSettings();
        settings.Sounds.Start = "DeviceConnect";

        var draft = settings.Clone();
        draft.Sounds.Start = "MailBeep";
        draft.Sounds.Volume = 10;

        Assert.Equal("DeviceConnect", settings.Sounds.Start);
        Assert.Equal(SoundCue.DefaultVolume, settings.Sounds.Volume);
    }
}
