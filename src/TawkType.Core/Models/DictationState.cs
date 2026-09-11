namespace TawkType.Core.Models;

public enum DictationState
{
    Idle,
    Listening,
    Transcribing,

    /// <summary>Running the LLM rewrite. Only entered when that pass is enabled and configured.</summary>
    Polishing,

    Injecting,
    Error,
}
