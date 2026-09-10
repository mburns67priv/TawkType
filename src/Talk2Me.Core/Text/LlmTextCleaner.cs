using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Settings;

namespace Talk2Me.Core.Text;

/// <summary>
/// The default cleaner. Always produces the regex-cleaned text, then — when the LLM pass is enabled and
/// configured — tries to replace it with a rewrite. Anything that goes wrong (no key, timeout, network,
/// rate limit, a reply that fails the sanity checks) falls back to the regex text, so dictation keeps
/// working when the network does not.
/// </summary>
public sealed class LlmTextCleaner : ITextCleaner
{
    /// <summary>
    /// A rewrite this much longer than the raw transcript means the model wrote something of its own —
    /// answered a dictated question, most likely. Discard it rather than type it.
    /// </summary>
    private const double MaxGrowthFactor = 3.0;

    private const int GrowthAllowanceChars = 60;

    private const int MaxOutputTokens = 4096;

    private readonly ILlmClient _llm;
    private readonly ISettingsProvider _settings;
    private readonly ILogger<LlmTextCleaner> _logger;

    public LlmTextCleaner(ILlmClient llm, ISettingsProvider settings, ILogger<LlmTextCleaner> logger)
    {
        _llm = llm;
        _settings = settings;
        _logger = logger;
    }

    public bool MayTakeAWhile => _settings.Current.Cleanup.UseLlm && _llm.IsConfigured;

    public async ValueTask<string> CleanAsync(string rawTranscript, CancellationToken cancellationToken = default)
    {
        var settings = _settings.Current;
        var basic = BasicTextCleaner.Clean(rawTranscript, settings.RemoveFillerWords);

        if (!settings.Cleanup.UseLlm || string.IsNullOrWhiteSpace(rawTranscript))
        {
            return basic;
        }

        if (!_llm.IsConfigured)
        {
            _logger.LogDebug("LLM cleanup is on but {Provider} is not configured; using regex cleanup", _llm.ProviderName);
            return basic;
        }

        var rewritten = await TryRewriteAsync(rawTranscript, settings.Cleanup, cancellationToken).ConfigureAwait(false);
        return rewritten ?? basic;
    }

    /// <summary>Returns the rewritten text, or null when the caller should fall back to the regex text.</summary>
    private async Task<string?> TryRewriteAsync(string raw, CleanupSettings cleanup, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(250, cleanup.TimeoutMs)));

        var started = Stopwatch.GetTimestamp();

        try
        {
            var request = new LlmRequest(
                CleanupPrompt.BuildSystemPrompt(cleanup),
                CleanupPrompt.BuildUserMessage(raw),
                cleanup.Model,
                MaxOutputTokens);

            var reply = await _llm.CompleteAsync(request, timeout.Token).ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(started);

            if (Validate(reply, raw) is not { } text)
            {
                return null;
            }

            _logger.LogDebug(
                "{Provider} rewrote {RawChars} chars to {CleanChars} in {Ms} ms",
                _llm.ProviderName,
                raw.Length,
                text.Length,
                (int)elapsed.TotalMilliseconds);

            return text;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("LLM cleanup timed out after {Ms} ms; typing regex-cleaned text", cleanup.TimeoutMs);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM cleanup failed; typing regex-cleaned text");
            return null;
        }
    }

    /// <summary>Strips wrappers the model may have added and rejects replies that do not look like a rewrite.</summary>
    private string? Validate(string reply, string raw)
    {
        var text = Unwrap(reply);

        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("LLM cleanup returned nothing for a {Chars}-char transcript; falling back", raw.Length);
            return null;
        }

        if (text.Length > (raw.Length * MaxGrowthFactor) + GrowthAllowanceChars)
        {
            _logger.LogWarning(
                "LLM cleanup returned {CleanChars} chars for a {RawChars}-char transcript; falling back",
                text.Length,
                raw.Length);
            return null;
        }

        return text;
    }

    private static string Unwrap(string reply)
    {
        var text = reply.Trim();

        // The prompt forbids these, but a model that ignores it should not put fences into the user's document.
        if (text.StartsWith(CleanupPrompt.OpenTag, StringComparison.OrdinalIgnoreCase))
        {
            text = text[CleanupPrompt.OpenTag.Length..];
        }

        if (text.EndsWith(CleanupPrompt.CloseTag, StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^CleanupPrompt.CloseTag.Length];
        }

        text = text.Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBreak = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstBreak > 0 && lastFence > firstBreak)
            {
                text = text[(firstBreak + 1)..lastFence];
            }
        }

        return text.Trim();
    }
}
