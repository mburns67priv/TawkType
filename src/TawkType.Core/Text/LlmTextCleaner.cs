using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TawkType.Core.Abstractions;
using TawkType.Core.Settings;

namespace TawkType.Core.Text;

/// <summary>
/// The default cleaner: regex cleanup, then the local phrase book, then — when the LLM pass is enabled
/// and configured — a rewrite. Anything that goes wrong (no key, timeout, network, rate limit, a reply
/// that fails the sanity checks) falls back to the local text, so dictation keeps working when the
/// network does not.
///
/// A dictation that expanded a snippet is never sent for rewriting. Snippets are exact by definition —
/// a signature, a URL, a template — and a model asked to tidy them up would do exactly that.
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

    public bool MayTakeAWhile
        => _settings.Current.Cleanup.UseLlm
            && _settings.Current.ActiveModeOrDefault().MayUseLlm
            && _llm.IsConfigured;

    public async ValueTask<string> CleanAsync(string rawTranscript, CancellationToken cancellationToken = default)
    {
        var settings = _settings.Current;
        var mode = settings.ActiveModeOrDefault();
        var cleaned = BasicTextCleaner.Clean(rawTranscript, mode);

        // The mode's own words on top of the main list, never instead of it: a correction the user has
        // taught TawkType should not stop applying because they picked a different mode.
        var vocabulary = settings.Vocabulary.With(mode.Vocabulary);

        var local = PhraseBook.Apply(cleaned, vocabulary);
        var basic = local.Text;

        if (local.ExpandedSnippet)
        {
            _logger.LogDebug("A snippet was inserted; typing it as written");
            return basic;
        }

        if (!settings.Cleanup.UseLlm || string.IsNullOrWhiteSpace(rawTranscript))
        {
            return basic;
        }

        // A mode can only ever narrow this. Picking one must never be what authorises text leaving the
        // machine — that decision belongs to the AI cleanup setting and nothing else.
        if (!mode.MayUseLlm)
        {
            _logger.LogDebug("The {Mode} mode does not use the rewrite pass", mode.Name);
            return basic;
        }

        if (!_llm.IsConfigured)
        {
            _logger.LogDebug("LLM cleanup is on but {Provider} is not configured; using regex cleanup", _llm.ProviderName);
            return basic;
        }

        var rewritten = await TryRewriteAsync(rawTranscript, settings, mode, vocabulary, cancellationToken)
            .ConfigureAwait(false);
        if (rewritten is null)
        {
            return basic;
        }

        // Applied again on the way out: the model works from the raw transcript, so without this a
        // rewrite would quietly undo every correction the user has written down.
        return PhraseBook.Apply(rewritten, vocabulary).Text;
    }

    /// <summary>Returns the rewritten text, or null when the caller should fall back to the regex text.</summary>
    private async Task<string?> TryRewriteAsync(
        string raw,
        TawkTypeSettings settings,
        DictationMode mode,
        VocabularySettings vocabulary,
        CancellationToken cancellationToken)
    {
        // The mode's tone wins over the settings page's: picking "Chat" is a statement about this
        // dictation, and it would be strange for it not to reach the one step that can act on it.
        var cleanup = settings.Cleanup.Clone();
        cleanup.Style = mode.Style;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(250, cleanup.TimeoutMs)));

        var started = Stopwatch.GetTimestamp();

        try
        {
            var request = new LlmRequest(
                CleanupPrompt.BuildSystemPrompt(cleanup, vocabulary.Spellings),
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
