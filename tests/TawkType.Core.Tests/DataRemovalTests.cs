using TawkType.Core.Settings;

namespace TawkType.Core.Tests;

/// <summary>
/// The guard in front of deleting everything TawkType has kept. One of its callers runs during an
/// uninstall with nobody watching, so these are the cases that would actually destroy something —
/// not a survey of malformed paths.
/// </summary>
public class DataRemovalTests
{
    private const string Data = @"C:\Users\someone\AppData\Local\TawkType";
    private const string Install = @"C:\Users\someone\AppData\Local\TawkTypeApp";

    [Fact]
    public void The_data_folder_can_be_deleted()
        => Assert.True(DataRemoval.Check(Data, Install).CanDelete);

    /// <summary>
    /// Gotcha 19, as an assertion. The two folders differ by a three-letter suffix, so a prefix
    /// comparison says the data folder contains the installation. It does not, and refusing here
    /// would mean the feature never works at all.
    /// </summary>
    [Fact]
    public void The_install_folder_next_door_is_not_inside_the_data_folder()
    {
        var check = DataRemoval.Check(Data, Install);

        Assert.True(check.CanDelete);
        Assert.Null(check.Refusal);
    }

    [Fact]
    public void The_install_folder_is_never_the_thing_deleted()
    {
        var check = DataRemoval.Check(Install, Install);

        Assert.False(check.CanDelete);
        Assert.Contains("installed", check.Refusal);
    }

    /// <summary>
    /// The shape of a genuine mix-up: a data path that resolves to the parent of the installation.
    /// Deleting it would take the running application with it, mid-uninstall.
    /// </summary>
    [Fact]
    public void A_folder_holding_the_installation_is_refused()
    {
        var check = DataRemoval.Check(@"C:\Users\someone\AppData\Local", Install);

        Assert.False(check.CanDelete);
        Assert.Contains("contains the installed application", check.Refusal);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\Users\someone")]
    public void Anything_near_the_root_of_the_drive_is_refused(string path)
        => Assert.False(DataRemoval.Check(path, Install).CanDelete);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_is_not_a_folder(string? path)
        => Assert.False(DataRemoval.Check(path, Install).CanDelete);

    [Fact]
    public void A_relative_path_is_refused()
        => Assert.False(DataRemoval.Check(@"TawkType\models", Install).CanDelete);

    /// <summary>Two spellings of one folder are one folder, trailing separator or not.</summary>
    [Fact]
    public void A_trailing_separator_does_not_change_the_answer()
    {
        Assert.False(DataRemoval.Check(Install + @"\", Install).CanDelete);
        Assert.False(DataRemoval.Check(Install, Install + @"\").CanDelete);
        Assert.True(DataRemoval.Check(Data + @"\", Install).CanDelete);
    }

    [Fact]
    public void Case_does_not_change_the_answer()
        => Assert.False(DataRemoval.Check(Install.ToUpperInvariant(), Install.ToLowerInvariant()).CanDelete);

    /// <summary>
    /// The uninstall hook may not know where it is installed. That is not a reason to refuse — the
    /// depth and shape checks still apply, and they are what stop the damaging cases.
    /// </summary>
    [Fact]
    public void An_unknown_install_folder_still_allows_a_sane_data_folder()
    {
        Assert.True(DataRemoval.Check(Data, null).CanDelete);
        Assert.False(DataRemoval.Check(@"C:\", null).CanDelete);
    }

    [Fact]
    public void A_refusal_always_says_why()
    {
        var check = DataRemoval.Check(@"C:\", Install);

        Assert.False(check.CanDelete);
        Assert.False(string.IsNullOrWhiteSpace(check.Refusal));
    }
}
