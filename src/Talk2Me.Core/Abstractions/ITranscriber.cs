using Talk2Me.Core.Models;

namespace Talk2Me.Core.Abstractions;

public interface ITranscriber : IAsyncDisposable
{
    /// <summary>
    /// True when the model for the engine that would run next is already on disk. False means every
    /// call below fails with <see cref="ModelNotDownloadedException"/> until the user downloads it.
    /// </summary>
    bool IsModelReady { get; }

    /// <summary>Loads the model and warms up the runtime so the first real transcription is fast.</summary>
    Task WarmUpAsync(IProgress<ModelProgress>? progress = null, CancellationToken cancellationToken = default);

    Task<TranscriptResult> TranscribeAsync(AudioClip clip, CancellationToken cancellationToken = default);
}
