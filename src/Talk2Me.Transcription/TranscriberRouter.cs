using Microsoft.Extensions.Logging;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Models;
using Talk2Me.Core.Settings;

namespace Talk2Me.Transcription;

/// <summary>
/// The <see cref="ITranscriber"/> the app talks to. Picks Parakeet or Whisper per settings and language,
/// so changing the engine in the UI takes effect on the next dictation.
/// </summary>
public sealed class TranscriberRouter : ITranscriber
{
    private readonly ParakeetTranscriber _parakeet;
    private readonly WhisperTranscriber _whisper;
    private readonly ISettingsProvider _settings;
    private readonly ILogger<TranscriberRouter> _logger;

    public TranscriberRouter(
        ParakeetTranscriber parakeet,
        WhisperTranscriber whisper,
        ISettingsProvider settings,
        ILogger<TranscriberRouter> logger)
    {
        _parakeet = parakeet;
        _whisper = whisper;
        _settings = settings;
        _logger = logger;
    }

    public TranscriptionEngine ActiveEngine
        => EngineSelection.Resolve(_settings.Current.Engine, _settings.Current.Language);

    public Task WarmUpAsync(IProgress<ModelProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var engine = ActiveEngine;
        _logger.LogInformation("Transcription engine: {Engine}", engine);
        return Select(engine).WarmUpAsync(progress, cancellationToken);
    }

    public Task<TranscriptResult> TranscribeAsync(AudioClip clip, CancellationToken cancellationToken = default)
        => Select(ActiveEngine).TranscribeAsync(clip, cancellationToken);

    /// <summary>Releases both engines' models from memory. They reload lazily on the next call.</summary>
    public async Task UnloadAsync()
    {
        await _parakeet.UnloadAsync().ConfigureAwait(false);
        await _whisper.UnloadAsync().ConfigureAwait(false);
        _logger.LogInformation("Transcription engines unloaded");
    }

    public async ValueTask DisposeAsync()
    {
        await _parakeet.DisposeAsync().ConfigureAwait(false);
        await _whisper.DisposeAsync().ConfigureAwait(false);
    }

    private ITranscriber Select(TranscriptionEngine engine)
        => engine == TranscriptionEngine.Parakeet ? _parakeet : _whisper;
}
