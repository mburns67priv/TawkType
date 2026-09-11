using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using TawkType.Core.Abstractions;

namespace TawkType.Llm;

/// <summary>
/// Rewrites transcripts with the Claude API. The key comes from <see cref="IApiKeyStore"/>, falling back
/// to the ANTHROPIC_API_KEY environment variable so the bench tool and CI can run without the settings UI.
/// </summary>
public sealed class ClaudeLlmClient : ILlmClient
{
    private readonly IApiKeyStore _keys;
    private readonly ILogger<ClaudeLlmClient> _logger;
    private readonly object _gate = new();

    private AnthropicClient? _client;
    private string? _clientKey;

    public ClaudeLlmClient(IApiKeyStore keys, ILogger<ClaudeLlmClient> logger)
    {
        _keys = keys;
        _logger = logger;
    }

    public string ProviderName => "Claude";

    public bool IsConfigured => ResolveKey() is not null;

    public async Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetClient() ?? throw new InvalidOperationException("No Anthropic API key is configured.");

        var response = await client.Messages.Create(
            new MessageCreateParams
            {
                Model = request.Model,
                MaxTokens = request.MaxOutputTokens,
                System = request.SystemPrompt,

                // Cleanup is a small, well-specified rewrite: the lowest effort keeps latency inside the
                // caller's timeout. Thinking stays adaptive — disabling it on Opus can leak reasoning
                // markup into the visible reply, which here would be typed straight into the user's window.
                Thinking = new ThinkingConfigAdaptive(),
                OutputConfig = new OutputConfig { Effort = Effort.Low },

                Messages = [new() { Role = Role.User, Content = request.UserMessage }],
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (response.StopReason == "refusal")
        {
            _logger.LogWarning("Claude declined the rewrite: {Category}", response.StopDetails?.Category);
            return string.Empty;
        }

        return string.Concat(response.Content.Select(block => block.Value).OfType<TextBlock>().Select(block => block.Text));
    }

    private AnthropicClient? GetClient()
    {
        var key = ResolveKey();
        if (key is null)
        {
            return null;
        }

        lock (_gate)
        {
            if (_client is null || _clientKey != key)
            {
                // No retries: a retry inside the caller's ~2 s budget only delays the fallback.
                _client = new AnthropicClient { ApiKey = key, MaxRetries = 0 };
                _clientKey = key;
            }

            return _client;
        }
    }

    private string? ResolveKey()
    {
        var key = _keys.Read();
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key.Trim();
        }

        key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }
}
