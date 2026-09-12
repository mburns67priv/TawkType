using TawkType.Core.Settings;

namespace TawkType.Core.Tests;

public sealed class SettingsCloneTests
{
    /// <summary>
    /// The diagnostic log is on unless the user says otherwise: it is the only thing that explains a
    /// failure after the fact, it never records a word of what was said, and it is capped at two files.
    /// Being a plain bool rather than a section is also what keeps it out of gotcha 11's way.
    /// </summary>
    [Fact]
    public void The_diagnostic_log_is_on_by_default_and_copied_by_value()
    {
        var original = new TawkTypeSettings();
        Assert.True(original.WriteDiagnosticLog);

        var copy = original.Clone();
        copy.WriteDiagnosticLog = false;

        Assert.True(original.WriteDiagnosticLog);
    }

    /// <summary>
    /// Off, and it has to stay off. TawkType recognises speech, applies the phrase book, picks a mode
    /// and fits text to the caret without a network, so the one thing that would reach out on its own
    /// is this check — and a default of on would make "it connects when you choose" untrue on every
    /// machine that never opened Settings.
    /// </summary>
    [Fact]
    public void Automatic_update_checks_are_off_by_default()
    {
        Assert.False(new TawkTypeSettings().CheckForUpdatesAutomatically);
    }

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
