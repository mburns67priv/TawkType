using Murmur.Core.Models;

namespace Murmur.Core.Abstractions;

public interface ITranscriber : IAsyncDisposable
{
    /// <summary>Downloads/loads the model and warms up the runtime so the first real transcription is fast.</summary>
    Task WarmUpAsync(IProgress<ModelProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<TranscriptResult> TranscribeAsync(AudioClip clip, CancellationToken cancellationToken = default);
}
