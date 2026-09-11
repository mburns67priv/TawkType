using TawkType.Core.Onboarding;

namespace TawkType.Core.Tests;

/// <summary>
/// The order of first run, and what each step insists on before it lets go. The point of §8 is that
/// "ready" means it worked rather than that the user agreed it would, so every gate here is checked
/// against an observation.
/// </summary>
public class SetupPlanTests
{
    private static readonly SetupState Everything = new()
    {
        MicrophoneHeard = true,
        ModelDownloaded = true,
        ModelLoaded = true,
        HotkeyUsable = true,
        DictationSucceeded = true,
    };

    [Fact]
    public void The_flow_starts_at_the_welcome_and_ends_at_done()
    {
        Assert.Equal(SetupStep.Welcome, SetupPlan.Steps[0]);
        Assert.Equal(SetupStep.Done, SetupPlan.Steps[^1]);
        Assert.Null(SetupPlan.Next(SetupStep.Done));
        Assert.Null(SetupPlan.Back(SetupStep.Welcome));
    }

    [Fact]
    public void Every_step_can_be_walked_forwards_and_back_again()
    {
        foreach (var step in SetupPlan.Steps)
        {
            if (SetupPlan.Next(step) is { } next)
            {
                Assert.Equal(step, SetupPlan.Back(next));
            }
        }
    }

    [Fact]
    public void The_practice_dictation_comes_after_everything_it_needs()
    {
        var order = SetupPlan.Steps;

        Assert.True(order.ToList().IndexOf(SetupStep.Microphone) < order.ToList().IndexOf(SetupStep.Practice));
        Assert.True(order.ToList().IndexOf(SetupStep.Model) < order.ToList().IndexOf(SetupStep.Practice));
        Assert.True(order.ToList().IndexOf(SetupStep.Hotkey) < order.ToList().IndexOf(SetupStep.Practice));
    }

    [Fact]
    public void Nothing_stands_in_the_way_when_everything_has_worked()
    {
        foreach (var step in SetupPlan.Steps)
        {
            Assert.True(SetupPlan.Check(step, Everything).CanContinue, step.ToString());
        }

        Assert.True(Everything.IsReady);
    }

    [Fact]
    public void A_microphone_nobody_has_heard_holds_the_flow_up()
    {
        var gate = SetupPlan.Check(SetupStep.Microphone, Everything with { MicrophoneHeard = false });

        Assert.False(gate.CanContinue);
        Assert.Contains("meter", gate.Blocker);
    }

    /// <summary>
    /// A download is not an engine. The model file can be on disk and still fail to initialise — a
    /// missing runtime, a truncated file — and a setup that called that "ready" would be lying at the
    /// one moment the user is deciding whether the app works.
    /// </summary>
    [Fact]
    public void A_downloaded_model_that_has_not_loaded_is_not_ready()
    {
        var state = Everything with { ModelLoaded = false };

        Assert.False(state.ModelReady);
        Assert.False(state.IsReady);
        Assert.False(SetupPlan.Check(SetupStep.Model, state).CanContinue);
    }

    [Fact]
    public void A_missing_model_is_named_before_a_missing_load()
    {
        var gate = SetupPlan.Check(SetupStep.Model, Everything with { ModelDownloaded = false, ModelLoaded = false });

        Assert.Contains("Download", gate.Blocker);
    }

    [Fact]
    public void Setup_is_not_finished_until_a_dictation_has_actually_worked()
    {
        var state = Everything with { DictationSucceeded = false };

        Assert.False(state.IsReady);
        Assert.False(SetupPlan.Check(SetupStep.Practice, state).CanContinue);
    }

    /// <summary>Nothing before the practice step depends on the dictation that step produces.</summary>
    [Theory]
    [InlineData(SetupStep.Welcome)]
    [InlineData(SetupStep.Microphone)]
    [InlineData(SetupStep.Model)]
    [InlineData(SetupStep.Hotkey)]
    public void Earlier_steps_do_not_wait_on_the_practice_dictation(SetupStep step)
        => Assert.True(SetupPlan.Check(step, Everything with { DictationSucceeded = false }).CanContinue);

    [Fact]
    public void A_blocked_step_always_says_why()
    {
        foreach (var step in SetupPlan.Steps)
        {
            var gate = SetupPlan.Check(step, new SetupState());
            Assert.Equal(gate.CanContinue, gate.Blocker is null);
        }
    }

    [Fact]
    public void Steps_are_numbered_from_one()
    {
        Assert.Equal(1, SetupPlan.Number(SetupStep.Welcome));
        Assert.Equal(SetupPlan.Count, SetupPlan.Number(SetupStep.Done));
    }
}
