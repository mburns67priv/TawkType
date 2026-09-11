using Microsoft.Extensions.Logging;
using TawkType.Core.Models;
using TawkType.Core.Settings;

namespace TawkType.Transcription;

/// <summary>
/// Downloads the sherpa-onnx int8 export of NVIDIA Parakeet TDT 0.6B v3 (encoder, decoder, joiner, tokens)
/// into %LOCALAPPDATA%/TawkType/models/parakeet-tdt-0.6b-v3-int8 on first use.
/// </summary>
public sealed class ParakeetModelManager
{
    public const string ModelName = "parakeet-tdt-0.6b-v3-int8";

    private const string BaseUrl = "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/";
    private const long OneMegabyte = 1024 * 1024;

    // Name -> approximate size, used for progress when Content-Length is missing.
    /// <summary>
    /// Rounded up a little: these drive the progress bar before anything has been downloaded, and a
    /// bar that reaches 100% early looks broken in a way one that arrives slightly under does not. The
    /// real files come to about 640 MB, not the 671 these add up to — do not quote them as sizes.
    /// </summary>
    private static readonly (string Name, long ApproxBytes)[] Files =
    [
        ("encoder.int8.onnx", 652 * OneMegabyte),
        ("decoder.int8.onnx", 12 * OneMegabyte),
        ("joiner.int8.onnx", 7 * OneMegabyte),
        ("tokens.txt", 94 * 1024),
    ];

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    private readonly ILogger<ParakeetModelManager> _logger;
    private readonly SemaphoreSlim _downloadLock = new(1, 1);

    public ParakeetModelManager(ILogger<ParakeetModelManager> logger)
    {
        _logger = logger;
    }

    public string ModelDirectory { get; } = Path.Combine(SettingsStore.AppDataDirectory, "models", ModelName);

    public string EncoderPath => Path.Combine(ModelDirectory, "encoder.int8.onnx");

    public string DecoderPath => Path.Combine(ModelDirectory, "decoder.int8.onnx");

    public string JoinerPath => Path.Combine(ModelDirectory, "joiner.int8.onnx");

    public string TokensPath => Path.Combine(ModelDirectory, "tokens.txt");

    public bool IsDownloaded => Files.All(f => File.Exists(Path.Combine(ModelDirectory, f.Name)));

    public async Task EnsureModelAsync(IProgress<ModelProgress>? progress, CancellationToken cancellationToken)
    {
        if (IsDownloaded)
        {
            return;
        }

        await _downloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsDownloaded)
            {
                return;
            }

            Directory.CreateDirectory(ModelDirectory);
            var total = Files.Sum(f => f.ApproxBytes);
            long doneBefore = 0;
            _logger.LogInformation("Downloading Parakeet model to {Dir}", ModelDirectory);

            foreach (var (name, approxBytes) in Files)
            {
                var target = Path.Combine(ModelDirectory, name);
                if (File.Exists(target))
                {
                    doneBefore += approxBytes;
                    continue;
                }

                await DownloadFileAsync(name, target, doneBefore, total, progress, cancellationToken).ConfigureAwait(false);
                doneBefore += approxBytes;
            }

            progress?.Report(new ModelProgress("Downloading model", total, total));
            _logger.LogInformation("Parakeet model ready");
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    private async Task DownloadFileAsync(
        string name, string target, long doneBefore, long total, IProgress<ModelProgress>? progress, CancellationToken cancellationToken)
    {
        var partial = target + ".part";
        try
        {
            using var response = await Http.GetAsync(BaseUrl + name, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var destination = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

            var buffer = new byte[1 << 20];
            long done = 0;
            long lastReported = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                done += read;
                if (done - lastReported >= 4 * OneMegabyte)
                {
                    lastReported = done;
                    progress?.Report(new ModelProgress("Downloading model", doneBefore + done, total));
                }
            }
        }
        catch
        {
            TryDelete(partial);
            throw;
        }

        File.Move(partial, target, overwrite: true);
        _logger.LogInformation("Downloaded {Name} ({Size} MB)", name, new FileInfo(target).Length / OneMegabyte);
    }

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
