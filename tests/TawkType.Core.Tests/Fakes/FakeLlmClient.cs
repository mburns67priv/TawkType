using TawkType.Core.Abstractions;

namespace TawkType.Core.Tests.Fakes;

public sealed class FakeLlmClient : ILlmClient
{
    public string ProviderName => "Fake";

    public bool IsConfigured { get; set; } = true;

    public string ReplyToReturn { get; set; } = string.Empty;

    /// <summary>Delay before replying, so the cleaner's timeout can be exercised.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public Exception? ExceptionToThrow { get; set; }

    public List<LlmRequest> Received { get; } = new();

    public async Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        Received.Add(request);

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return ReplyToReturn;
    }
}
