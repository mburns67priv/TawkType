using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Models;
using SherpaOnnx;

namespace Talk2Me.Transcription;

/// <summary>
/// NVIDIA Parakeet TDT 0.6B v3 through sherpa-onnx (ONNX Runtime, CPU int8). A transducer model:
/// several times faster than Whisper, better English accuracy, and it does not hallucinate on silence.
/// Covers 25 European languages and auto-detects among them.
/// </summary>
public sealed class ParakeetTranscriber : ITranscriber
{
    private static readonly TimeSpan MinimumClipLength = TimeSpan.FromMilliseconds(500);

    private readonly ParakeetModelManager _models;
    private readonly ILogger<ParakeetTranscriber> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private OfflineRecognizer? _recognizer;

    public ParakeetTranscriber(ParakeetModelManager models, ILogger<ParakeetTranscriber> logger)
    {
        _models = models;
        _logger = logger;
    }

    public bool IsLoaded => _recognizer is not null;

    public bool IsModelReady => _models.IsDownloaded;

    public async Task WarmUpAsync(IProgress<ModelProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(progress, cancellationToken).ConfigureAwait(false);

        progress?.Report(new ModelProgress("Warming up", 0, null));
        var silence = new float[AudioClip.WhisperSampleRate * 2];
        await TranscribeCoreAsync(silence, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Parakeet warm-up complete");
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
            Array.Resize(ref samples, minimumSamples);
        }

        var stopwatch = Stopwatch.StartNew();
        var (text, language) = await TranscribeCoreAsync(samples, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        _logger.LogDebug("Parakeet transcribed {Seconds:F1}s in {Ms} ms: {Text}", clip.Duration.TotalSeconds, stopwatch.ElapsedMilliseconds, text);
        return new TranscriptResult(text, stopwatch.Elapsed, language);
    }

    /// <summary>Releases the model from memory; the next transcription reloads it.</summary>
    public async Task UnloadAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _recognizer?.Dispose();
            _recognizer = null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync() => await UnloadAsync().ConfigureAwait(false);

    private async Task<(string Text, string? Language)> TranscribeCoreAsync(float[] samples, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var recognizer = _recognizer ?? throw new InvalidOperationException("Model not loaded");

            // sherpa-onnx is synchronous and CPU-bound; keep it off the caller's thread.
            return await Task.Run(
                () =>
                {
                    using var stream = recognizer.CreateStream();
                    stream.AcceptWaveform(AudioClip.WhisperSampleRate, samples);
                    recognizer.Decode(stream);
                    // The C# binding exposes no detected-language field, so leave it null.
                    return (stream.Result.Text.Trim(), (string?)null);
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureLoadedAsync(IProgress<ModelProgress>? progress, CancellationToken cancellationToken)
    {
        if (_recognizer is not null)
        {
            return;
        }

        if (!_models.IsDownloaded)
        {
            throw new ModelNotDownloadedException("Parakeet");
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_recognizer is not null)
            {
                return;
            }

            progress?.Report(new ModelProgress("Loading model", 0, null));
            var stopwatch = Stopwatch.StartNew();

            var config = new OfflineRecognizerConfig();
            config.FeatConfig.SampleRate = AudioClip.WhisperSampleRate;
            config.ModelConfig.Transducer.Encoder = _models.EncoderPath;
            config.ModelConfig.Transducer.Decoder = _models.DecoderPath;
            config.ModelConfig.Transducer.Joiner = _models.JoinerPath;
            config.ModelConfig.Tokens = _models.TokensPath;
            config.ModelConfig.ModelType = "nemo_transducer";
            config.ModelConfig.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
            config.ModelConfig.Provider = "cpu";
            config.DecodingMethod = "greedy_search";

            _recognizer = await Task.Run(() => new OfflineRecognizer(config), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Loaded Parakeet TDT 0.6B v3 (int8, CPU, {Threads} threads) in {Ms} ms", config.ModelConfig.NumThreads, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            _lock.Release();
        }
    }
}
