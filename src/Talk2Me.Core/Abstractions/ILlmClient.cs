namespace Talk2Me.Core.Abstractions;

/// <summary>One rewrite request: a stable system prompt plus the raw transcript to clean up.</summary>
/// <param name="SystemPrompt">Instructions for the rewrite. Stable across dictations for a given settings state.</param>
/// <param name="UserMessage">The transcript, already wrapped in whatever delimiters the prompt expects.</param>
/// <param name="Model">Provider-specific model identifier.</param>
/// <param name="MaxOutputTokens">Ceiling for the rewritten text.</param>
public readonly record struct LlmRequest(string SystemPrompt, string UserMessage, string Model, int MaxOutputTokens);

/// <summary>
/// A single-turn text completion. Deliberately minimal so Core stays free of any provider SDK:
/// the Claude-backed implementation lives in Talk2Me.Llm, and a local-model one can slot in beside it.
/// </summary>
public interface ILlmClient
{
    /// <summary>True when the client has what it needs to make a call (an API key, a loaded model, …).</summary>
    bool IsConfigured { get; }

    /// <summary>Human-readable name for logs and the settings window, e.g. "Claude".</summary>
    string ProviderName { get; }

    /// <summary>Returns the completion text. Throws on transport, auth, or provider errors.</summary>
    Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
}
