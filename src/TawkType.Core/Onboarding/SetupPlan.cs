namespace TawkType.Core.Onboarding;

/// <summary>
/// The stages of first run, in the order they happen. Everything before <see cref="Practice"/> exists
/// to make that step possible, and the flow is finished only once it has worked.
/// </summary>
public enum SetupStep
{
    /// <summary>What TawkType is, and what it does with what you say.</summary>
    Welcome,

    /// <summary>Choose an input device and watch the meter move.</summary>
    Microphone,

    /// <summary>Choose a language, then download and load the model it needs.</summary>
    Model,

    /// <summary>Record the key that will be held to dictate.</summary>
    Hotkey,

    /// <summary>Dictate into a box TawkType owns, so a failure here is ours and not the user's.</summary>
    Practice,

    /// <summary>What is kept on disk, and what may leave the machine.</summary>
    Privacy,

    /// <summary>What is true now, and the way back to all of it.</summary>
    Done,
}

/// <summary>
/// Whether the flow may move on from a step, and what is missing when it may not. A string rather
/// than a flag because the only useful thing to show beside a disabled Continue is the reason.
/// </summary>
public readonly record struct SetupGate(bool CanContinue, string? Blocker)
{
    public static SetupGate Open { get; } = new(true, null);

    public static SetupGate Closed(string because) => new(false, because);
}

/// <summary>
/// What first run has actually established. Every field is something observed — a level that moved, a
/// model that loaded, words that came back — never something the user merely agreed to. That is the
/// whole distinction §8 of the feature research draws: "ready" has to mean it worked.
/// </summary>
public sealed record SetupState
{
    /// <summary>The meter moved above the noise floor while the user was speaking.</summary>
    public bool MicrophoneHeard { get; init; }

    /// <summary>The model files for the chosen language's engine are on disk.</summary>
    public bool ModelDownloaded { get; init; }

    /// <summary>And the engine initialised from them, which is the half a download does not prove.</summary>
    public bool ModelLoaded { get; init; }

    /// <summary>A combination TawkType can bind. Not the same as a wise one; see <see cref="HotkeyCheck"/>.</summary>
    public bool HotkeyUsable { get; init; }

    /// <summary>A real dictation produced words. The point of the whole flow.</summary>
    public bool DictationSucceeded { get; init; }

    public bool ModelReady => ModelDownloaded && ModelLoaded;

    /// <summary>Everything the first dictation needed, all of it observed rather than assumed.</summary>
    public bool IsReady => MicrophoneHeard && ModelReady && HotkeyUsable && DictationSucceeded;
}

/// <summary>
/// The order of first run and the conditions for leaving each step. Pure, so the awkward part — what
/// counts as done, and what to say when it is not — is testable without a microphone or a model.
/// </summary>
public static class SetupPlan
{
    private static readonly SetupStep[] Order = Enum.GetValues<SetupStep>();

    public static IReadOnlyList<SetupStep> Steps => Order;

    public static int Count => Order.Length;

    /// <summary>1-based, for "Step 3 of 7".</summary>
    public static int Number(SetupStep step) => Array.IndexOf(Order, step) + 1;

    public static SetupStep? Next(SetupStep step)
    {
        var index = Array.IndexOf(Order, step);
        return index >= 0 && index < Order.Length - 1 ? Order[index + 1] : null;
    }

    public static SetupStep? Back(SetupStep step)
    {
        var index = Array.IndexOf(Order, step);
        return index > 0 ? Order[index - 1] : null;
    }

    /// <summary>
    /// Whether this step has been satisfied, and a few words on what is missing when it is not. Short
    /// words: this sits on one line beside the buttons, and the step itself has already explained the
    /// task at length. A gate is never opened by the user asserting something:
    /// the microphone gate wants a level, the model gate wants an engine that loaded, and the practice
    /// gate wants words that came back from one.
    /// </summary>
    public static SetupGate Check(SetupStep step, SetupState state) => step switch
    {
        SetupStep.Microphone when !state.MicrophoneHeard =>
            SetupGate.Closed("The meter has to move first."),

        SetupStep.Model when !state.ModelDownloaded =>
            SetupGate.Closed("Download the model first."),

        SetupStep.Model when !state.ModelLoaded =>
            SetupGate.Closed("Wait for the model to load."),

        SetupStep.Hotkey when !state.HotkeyUsable =>
            SetupGate.Closed("Record a combination TawkType can use."),

        SetupStep.Practice when !state.DictationSucceeded =>
            SetupGate.Closed("Setup ends with one dictation that works."),

        _ => SetupGate.Open,
    };
}
