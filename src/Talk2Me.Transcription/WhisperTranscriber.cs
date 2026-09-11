using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Models;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace Talk2Me.Transcription;

/// <summary>
/// Local speech-to-text through Whisper.net (whisper.cpp). The model and language are read from settings
/// and (re)loaded lazily, so changing them in the UI takes effect on the next dictation.
/// </summary>
public sealed partial class WhisperTranscriber : ITranscriber
{
    private static readonly TimeSpan MinimumClipLength = TimeSpan.FromMilliseconds(1500);

    private readonly ModelManager _models;
    private readonly ISettingsProvider _settings;
    private readonly ILogger<WhisperTranscriber> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private string? _loadedModelPath;
    private string? _loadedLanguage;

    static WhisperTranscriber()
    {
        // Prefer the GPU. Vulkan works with the stock NVIDIA/AMD/Intel driver, so it covers almost every
        // machine; the CUDA 12 backend is not shipped because the DLL is 538 MB and only helps users who
        // have installed the CUDA Toolkit. See Talk2Me.Transcription.csproj.
        RuntimeOptions.RuntimeLibraryOrder =
        [
            RuntimeLibrary.Vulkan,
            RuntimeLibrary.Cpu,
        ];
    }

    public WhisperTranscriber(ModelManager models, ISettingsProvider settings, ILogger<WhisperTranscriber> logger)
    {
        _models = models;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>The native backend that was actually loaded, once a model has been opened.</summary>
    public static RuntimeLibrary? ActiveRuntime => RuntimeOptions.LoadedLibrary;

    public bool IsModelReady => _models.IsDownloaded(ModelManager.ParseModelType(_settings.Current.Model));

    public async Task WarmUpAsync(IProgress<ModelProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(progress, cancellationToken).ConfigureAwait(false);

        progress?.Report(new ModelProgress("Warming up", 0, null));
        var silence = new float[AudioClip.WhisperSampleRate * 2];
        await TranscribeCoreAsync(silence, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Whisper warm-up complete on {Runtime}", ActiveRuntime);
    }

    public async Task<TranscriptResult> TranscribeAsync(AudioClip clip, CancellationToken cancellationToken = default)
    {
        if (clip.SampleRate != AudioClip.WhisperSampleRate)
        {
            throw new ArgumentException($"Expected {AudioClip.WhisperSampleRate} Hz audio, got {clip.SampleRate} Hz", nameof(clip));
        }

        await EnsureLoadedAsync(null, cancellationToken).ConfigureAwait(false);

        var samples = clip.Samples;
        var minimumSamples = (int)(MinimumClipLength.TotalSeconds * AudioClip.WhisperSampleRate);
        if (samples.Length < minimumSamples)
        {
            // whisper.cpp rejects very short input, so pad with silence.
            Array.Resize(ref samples, minimumSamples);
        }

        var stopwatch = Stopwatch.StartNew();
        var text = await TranscribeCoreAsync(samples, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        _logger.LogDebug("Transcribed {Seconds:F1}s in {Ms} ms: {Text}", clip.Duration.TotalSeconds, stopwatch.ElapsedMilliseconds, text);
        return new TranscriptResult(text, stopwatch.Elapsed, _loadedLanguage);
    }

    /// <summary>Releases the model from memory; the next transcription reloads it.</summary>
    public async Task UnloadAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await ReleaseModelAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync() => await UnloadAsync().ConfigureAwait(false);

    private async Task<string> TranscribeCoreAsync(float[] samples, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var processor = _processor ?? throw new InvalidOperationException("Model not loaded");
            var builder = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples, cancellationToken).ConfigureAwait(false))
            {
                builder.Append(segment.Text);
            }

            return NonSpeechTokens().Replace(builder.ToString(), string.Empty).Trim();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureLoadedAsync(IProgress<ModelProgress>? progress, CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        var type = ModelManager.ParseModelType(settings.Model);
        var language = string.IsNullOrWhiteSpace(settings.Language) ? "en" : settings.Language.Trim().ToLowerInvariant();

        if (!_models.IsDownloaded(type))
        {
            throw new ModelNotDownloadedException("Whisper " + type);
        }

        var path = _models.GetModelPath(type);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_processor is not null && path == _loadedModelPath && language == _loadedLanguage)
            {
                return;
            }

            await ReleaseModelAsync().ConfigureAwait(false);

            progress?.Report(new ModelProgress("Loading model", 0, null));
            var stopwatch = Stopwatch.StartNew();

            _factory = WhisperFactory.FromPath(path);
            var builder = _factory.CreateBuilder()
                .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 2, 8));
            builder = language == "auto" ? builder.WithLanguageDetection() : builder.WithLanguage(language);
            _processor = builder.Build();

            _loadedModelPath = path;
            _loadedLanguage = language;
            _logger.LogInformation(
                "Loaded whisper model {Model} ({Language}) on {Runtime} in {Ms} ms",
                type, language, ActiveRuntime, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task ReleaseModelAsync()
    {
        if (_processor is not null)
        {
            await _processor.DisposeAsync().ConfigureAwait(false);
            _processor = null;
        }

        _factory?.Dispose();
        _factory = null;
        _loadedModelPath = null;
        _loadedLanguage = null;
    }

    // whisper.cpp emits markers like [BLANK_AUDIO], [MUSIC], (silence) for non-speech audio.
    [GeneratedRegex(@"\[[A-Z_ ]+\]|\((?:silence|music|inaudible|applause|laughter)[^)]*\)", RegexOptions.IgnoreCase)]
    private static partial Regex NonSpeechTokens();
}
