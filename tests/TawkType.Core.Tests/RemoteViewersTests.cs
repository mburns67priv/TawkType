using TawkType.Core.Input;
using TawkType.Core.Settings;
using TawkType.Core.Text;

namespace TawkType.Core.Tests;

public sealed class RemoteViewersTests
{
    /// <summary>
    /// TigerVNC's standalone download keeps its version in the file name, so the process the user is
    /// typing into is "vncviewer64-1.16.2", not "vncviewer". An exact match would miss the very
    /// install that prompted this.
    /// </summary>
    [Theory]
    [InlineData("vncviewer")]
    [InlineData("vncviewer64")]
    [InlineData("vncviewer64-1.16.2")]
    [InlineData("VNCVIEWER")]
    public void TigerVNC_is_a_remote_viewer_by_default_however_its_file_is_named(string process)
    {
        Assert.True(RemoteViewers.Matches(process, new TawkTypeSettings().RemoteViewerApps));
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("WindowsTerminal")]
    [InlineData("chrome")]
    public void Ordinary_apps_are_not(string process)
    {
        Assert.False(RemoteViewers.Matches(process, new TawkTypeSettings().RemoteViewerApps));
    }

    [Fact]
    public void Several_apps_can_be_listed()
    {
        Assert.True(RemoteViewers.Matches("mstsc", "vncviewer, mstsc"));
        Assert.True(RemoteViewers.Matches("vncviewer64", "vncviewer, mstsc"));
    }

    /// <summary>
    /// A prefix match makes an empty entry match everything. A trailing comma in a hand-edited
    /// settings file must not turn every app into a remote viewer and every dictation into key presses.
    /// </summary>
    [Theory]
    [InlineData("vncviewer,")]
    [InlineData(", vncviewer")]
    [InlineData("vncviewer,,")]
    [InlineData(" , ")]
    public void A_stray_comma_does_not_match_every_app(string list)
    {
        Assert.False(RemoteViewers.Matches("notepad", list));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_unknown_window_is_not_a_remote_viewer(string? process)
    {
        Assert.False(RemoteViewers.Matches(process, RemoteViewers.Default));
    }

    [Fact]
    public void An_empty_list_turns_the_feature_off()
    {
        Assert.False(RemoteViewers.Matches("vncviewer", ""));
    }

    /// <summary>
    /// The default has to parse, or every remote paste would fall back with a warning in the log.
    /// Omarchy pastes with Super + V, which is the Windows key on this side of the viewer.
    /// </summary>
    [Fact]
    public void The_default_paste_key_is_Windows_plus_V()
    {
        Assert.True(Hotkey.TryParse(new TawkTypeSettings().RemotePasteKey, out var key));
        Assert.Equal([VirtualKey.LeftWindows], key.Modifiers);
        Assert.Equal('V', key.Key);
    }

    [Fact]
    public void Clone_copies_the_remote_settings_by_value()
    {
        var original = new TawkTypeSettings();
        var copy = original.Clone();

        copy.RemoteViewerApps = "mstsc";
        copy.RemotePasteKey = "Ctrl + Shift + V";

        Assert.Equal(RemoteViewers.Default, original.RemoteViewerApps);
        Assert.Equal(RemoteViewers.DefaultPasteKey, original.RemotePasteKey);
    }
}
