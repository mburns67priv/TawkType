using TawkType.Core.Settings;

namespace TawkType.Core.Tests;

public sealed class SettingsCloneTests
{
    [Fact]
    public void Clone_does_not_share_the_cleanup_section()
    {
        var original = new TawkTypeSettings();
        original.Cleanup.Vocabulary = ["Parakeet"];

        var copy = original.Clone();
        copy.Cleanup.UseLlm = true;
        copy.Cleanup.Vocabulary[0] = "Whisper";

        Assert.False(original.Cleanup.UseLlm);
        Assert.Equal("Parakeet", original.Cleanup.Vocabulary[0]);
    }

    [Fact]
    public void Clone_does_not_share_the_history_section()
    {
        var original = new TawkTypeSettings();

        var copy = original.Clone();
        copy.History.Enabled = false;
        copy.History.MaxEntries = 5;

        Assert.True(original.History.Enabled);
        Assert.Equal(200, original.History.MaxEntries);
    }

    [Fact]
    public void Clone_does_not_share_the_appearance_section()
    {
        var original = new TawkTypeSettings();

        var copy = original.Clone();
        copy.Appearance.Theme = AppTheme.Dark;

        Assert.Equal(AppTheme.System, original.Appearance.Theme);
    }

    [Fact]
    public void Clone_does_not_share_the_overlay_section()
    {
        var original = new TawkTypeSettings();

        var copy = original.Clone();
        copy.Overlay.AlwaysVisible = false;
        copy.Overlay.Position = OverlayPosition.TopRight;

        Assert.True(original.Overlay.AlwaysVisible);
        Assert.Equal(OverlayPosition.BottomCenter, original.Overlay.Position);
    }
}
