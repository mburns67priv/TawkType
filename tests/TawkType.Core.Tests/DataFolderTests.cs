using System.Text.RegularExpressions;
using TawkType.Core.Settings;

namespace TawkType.Core.Tests;

/// <summary>
/// Where user data lives. These are the assertions the rename could have broken silently: a folder
/// that collides with the install directory loses everything on the next update, and one that moved
/// without migration looks to the user exactly like a factory reset.
/// </summary>
public class DataFolderTests
{
    private static readonly string LocalAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    [Fact]
    public void Data_lives_directly_under_local_app_data()
        => Assert.Equal(Path.Combine(LocalAppData, "TawkType"), SettingsStore.AppDataDirectory);

    /// <summary>
    /// The one assertion holding the whole layout up.
    ///
    /// Velopack installs to %LOCALAPPDATA%\&lt;packId&gt; and *clears that folder first*. With the data
    /// folder flat under %LOCALAPPDATA%, only the "App" suffix keeps the two apart — so the packId in
    /// build/pack.ps1 must never be shortened to match this. Getting it wrong deletes settings, the
    /// encrypted API key, the history and gigabytes of models on every install, before the app runs.
    /// It happened once already, under the old name.
    /// </summary>
    [Fact]
    public void Data_does_not_sit_where_the_installer_extracts()
    {
        Assert.NotEqual(
            Path.Combine(LocalAppData, PackId),
            SettingsStore.AppDataDirectory,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the id out of the pack script rather than restating it, so the test fails if somebody
    /// changes the packaging without changing the data folder — which is the mistake it exists to catch.
    /// </summary>
    private static string PackId
    {
        get
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "TawkType.sln")))
            {
                root = root.Parent;
            }

            Assert.NotNull(root);

            var script = File.ReadAllText(Path.Combine(root!.FullName, "build", "pack.ps1"));
            var match = Regex.Match(script, @"--packId\s+(\S+)");
            Assert.True(match.Success, "build/pack.ps1 no longer declares --packId");
            return match.Groups[1].Value;
        }
    }

    [Fact]
    public void The_settings_file_sits_in_it()
        => Assert.Equal(Path.Combine(SettingsStore.AppDataDirectory, "settings.json"), SettingsStore.DefaultPath);
}
