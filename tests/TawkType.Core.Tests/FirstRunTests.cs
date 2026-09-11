using TawkType.Core.Settings;

namespace TawkType.Core.Tests;

/// <summary>
/// Who gets shown the first-run flow. It is for a machine that has never run TawkType — not for
/// everybody who happens to update to the version that added it.
/// </summary>
public class FirstRunTests
{
    [Fact]
    public void A_machine_with_no_settings_file_needs_setup()
        => Assert.True(new TawkTypeSettings().NeedsSetup);

    /// <summary>
    /// A settings file that is already on disk means TawkType has run and been configured here, so an
    /// update must not walk the user through onboarding to tell them what they already set up.
    /// </summary>
    [Fact]
    public void A_settings_file_written_before_setup_existed_counts_as_done()
    {
        var loaded = SettingsStore.Migrate(new TawkTypeSettings { SetupCompleted = null });

        Assert.True(loaded.SetupCompleted);
        Assert.False(loaded.NeedsSetup);
    }

    /// <summary>
    /// Quitting halfway through leaves a file behind — setup saves each answer as it is given — so the
    /// explicit "not finished" has to survive the migration that reads a missing answer as finished.
    /// </summary>
    [Fact]
    public void Setup_abandoned_halfway_comes_back_next_time()
    {
        var loaded = SettingsStore.Migrate(new TawkTypeSettings { SetupCompleted = false });

        Assert.True(loaded.NeedsSetup);
    }

    [Fact]
    public void Finished_setup_stays_finished()
        => Assert.False(SettingsStore.Migrate(new TawkTypeSettings { SetupCompleted = true }).NeedsSetup);

    [Fact]
    public void The_answer_survives_a_clone()
    {
        var settings = new TawkTypeSettings { SetupCompleted = true };

        Assert.False(settings.Clone().NeedsSetup);
    }
}
