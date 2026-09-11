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
    public void Data_lives_under_the_vendor_folder()
        => Assert.Equal(Path.Combine(LocalAppData, "Jupitor Studio", "TawkType"), SettingsStore.AppDataDirectory);

    /// <summary>
    /// The installer clears %LOCALAPPDATA%\&lt;packId&gt; before extracting. If the data folder were
    /// that same path, every update would delete the settings, the history and gigabytes of models
    /// before the app ever ran. The vendor folder is what keeps the two apart.
    /// </summary>
    [Fact]
    public void Data_does_not_sit_where_the_installer_extracts()
    {
        foreach (var packId in new[] { "TawkType", "Talk2Me", "Talk2MeApp" })
        {
            Assert.NotEqual(
                Path.Combine(LocalAppData, packId),
                SettingsStore.AppDataDirectory,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_settings_file_sits_in_it()
        => Assert.Equal(Path.Combine(SettingsStore.AppDataDirectory, "settings.json"), SettingsStore.DefaultPath);
}
