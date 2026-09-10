using Microsoft.Extensions.Logging;
using Talk2Me.Core.Models;
using Talk2Me.Core.Settings;
using Whisper.net.Ggml;

namespace Talk2Me.Transcription;

/// <summary>Downloads whisper.cpp ggml models into %LOCALAPPDATA%/Talk2Me/models on first use.</summary>
public sealed class ModelManager
{
    private const long OneMegabyte = 1024 * 1024;

    // Rough sizes for progress reporting when the server sends no Content-Length.
    private static readonly Dictionary<GgmlType, long> ApproximateSizes = new()
    {
        [GgmlType.Tiny] = 78 * OneMegabyte,
        [GgmlType.TinyEn] = 78 * OneMegabyte,
        [GgmlType.Base] = 148 * OneMegabyte,
        [GgmlType.BaseEn] = 148 * OneMegabyte,
        [GgmlType.Small] = 488 * OneMegabyte,
        [GgmlType.SmallEn] = 488 * OneMegabyte,
        [GgmlType.Medium] = 1530 * OneMegabyte,
        [GgmlType.MediumEn] = 1530 * OneMegabyte,
        [GgmlType.LargeV1] = 3090 * OneMegabyte,
        [GgmlType.LargeV2] = 3090 * OneMegabyte,
        [GgmlType.LargeV3] = 3090 * OneMegabyte,
        [GgmlType.LargeV3Turbo] = 1620 * OneMegabyte,
    };

    private readonly ILogger<ModelManager> _logger;
    private readonly SemaphoreSlim _downloadLock = new(1, 1);

    public ModelManager(ILogger<ModelManager> logger)
    {
        _logger = logger;
    }

    public string ModelsDirectory { get; } = Path.Combine(SettingsStore.AppDataDirectory, "models");

    public static IReadOnlyList<string> ModelNames { get; } = Enum.GetNames<GgmlType>();

    public static GgmlType ParseModelType(string? name)
        => Enum.TryParse<GgmlType>(name, ignoreCase: true, out var type) ? type : GgmlType.LargeV3Turbo;

    public string GetModelPath(GgmlType type) => Path.Combine(ModelsDirectory, $"ggml-{FileNameFor(type)}.bin");

    public bool IsDownloaded(GgmlType type) => File.Exists(GetModelPath(type));

    public async Task<string> EnsureModelAsync(GgmlType type, IProgress<ModelProgress>? progress, CancellationToken cancellationToken)
    {
        var path = GetModelPath(type);
        if (File.Exists(path))
        {
            return path;
        }

        await _downloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
            {
                return path;
            }

            Directory.CreateDirectory(ModelsDirectory);
            var partial = path + ".part";
            _logger.LogInformation("Downloading whisper model {Model} to {Path}", type, path);

            try
            {
                await using var source = await WhisperGgmlDownloader.Default
                    .GetGgmlModelAsync(type, QuantizationType.NoQuantization, cancellationToken)
                    .ConfigureAwait(false);

                long? total = source.CanSeek ? source.Length : ApproximateSizes.GetValueOrDefault(type);
                await using var destination = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

                var buffer = new byte[1 << 20];
                long done = 0;
                long lastReported = 0;
                int read;
                progress?.Report(new ModelProgress("Downloading model", 0, total));

                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    done += read;

                    if (done - lastReported >= 4 * OneMegabyte)
                    {
                        lastReported = done;
                        progress?.Report(new ModelProgress("Downloading model", done, total));
                    }
                }

                progress?.Report(new ModelProgress("Downloading model", done, done));
            }
            catch
            {
                TryDelete(partial);
                throw;
            }

            File.Move(partial, path, overwrite: true);
            _logger.LogInformation("Model {Model} ready ({Size} MB)", type, new FileInfo(path).Length / OneMegabyte);
            return path;
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    private static string FileNameFor(GgmlType type) => type switch
    {
        GgmlType.Tiny => "tiny",
        GgmlType.TinyEn => "tiny.en",
        GgmlType.Base => "base",
        GgmlType.BaseEn => "base.en",
        GgmlType.Small => "small",
        GgmlType.SmallEn => "small.en",
        GgmlType.Medium => "medium",
        GgmlType.MediumEn => "medium.en",
        GgmlType.LargeV1 => "large-v1",
        GgmlType.LargeV2 => "large-v2",
        GgmlType.LargeV3 => "large-v3",
        GgmlType.LargeV3Turbo => "large-v3-turbo",
        _ => type.ToString().ToLowerInvariant(),
    };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best effort
        }
    }
}
