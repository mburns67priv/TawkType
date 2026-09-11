using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TawkType.Core.Abstractions;
using TawkType.Core.Models;
using TawkType.Core.Settings;
using TawkType.Core.Text;
using TawkType.Transcription;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

if (args.Length == 0)
{
    Console.WriteLine("usage: TawkType.Bench <audio.wav> [engine=Both|Parakeet|Whisper] [runs=3] [language=en] [whisperModel=LargeV3Turbo]");
    return 1;
}

var wavPath = args[0];
var engineArg = args.Length > 1 ? args[1] : "Both";
var runs = args.Length > 2 ? int.Parse(args[2]) : 3;
var language = args.Length > 3 ? args[3] : "en";
var whisperModel = args.Length > 4 ? args[4] : "LargeV3Turbo";

using var loggerFactory = LoggerFactory.Create(b => b
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss.fff "; })
    .SetMinimumLevel(LogLevel.Information));

var settings = new StaticSettings(new TawkTypeSettings { Model = whisperModel, Language = language });
var clip = LoadClip(wavPath);
Console.WriteLine($"Clip: {clip.Duration.TotalSeconds:F1}s @ {clip.SampleRate} Hz ({Path.GetFileName(wavPath)})");
Console.WriteLine($"CPU: {Environment.ProcessorCount} logical cores");
Console.WriteLine();

var engines = engineArg.ToLowerInvariant() switch
{
    "parakeet" => new[] { TranscriptionEngine.Parakeet },
    "whisper" => new[] { TranscriptionEngine.Whisper },
    _ => new[] { TranscriptionEngine.Parakeet, TranscriptionEngine.Whisper },
};

var summary = new List<(string Engine, double LoadMs, double BestMs, string Text)>();

foreach (var engine in engines)
{
    await using ITranscriber transcriber = engine == TranscriptionEngine.Parakeet
        ? new ParakeetTranscriber(
            new ParakeetModelManager(loggerFactory.CreateLogger<ParakeetModelManager>()),
            loggerFactory.CreateLogger<ParakeetTranscriber>())
        : new WhisperTranscriber(
            new ModelManager(loggerFactory.CreateLogger<ModelManager>()),
            settings,
            loggerFactory.CreateLogger<WhisperTranscriber>());

    var label = engine == TranscriptionEngine.Parakeet
        ? "Parakeet TDT 0.6B v3 (int8, CPU)"
        : $"Whisper {whisperModel}";

    Console.WriteLine($"=== {label} ===");
    var stopwatch = Stopwatch.StartNew();
    await transcriber.WarmUpAsync(new ConsoleProgress());
    var warmMs = stopwatch.Elapsed.TotalMilliseconds;
    var backend = engine == TranscriptionEngine.Whisper ? $" backend = {WhisperTranscriber.ActiveRuntime}" : string.Empty;
    Console.WriteLine($"Warm-up (download + load + first inference): {warmMs:F0} ms{backend}");

    var best = double.MaxValue;
    var text = string.Empty;
    for (var i = 1; i <= runs; i++)
    {
        var result = await transcriber.TranscribeAsync(clip);
        var ms = result.ProcessingTime.TotalMilliseconds;
        best = Math.Min(best, ms);
        text = result.Text;
        var realTime = clip.Duration.TotalMilliseconds / Math.Max(1, ms);
        Console.WriteLine($"Run {i}: {ms,6:F0} ms ({realTime,5:F1}x real-time)");
    }

    Console.WriteLine($"  raw:   {text}");
    Console.WriteLine($"  clean: {BasicTextCleaner.Clean(text, removeFillers: true)}");
    Console.WriteLine();
    summary.Add((label, warmMs, best, text));
}

if (summary.Count > 1)
{
    Console.WriteLine("=== Summary (best of runs) ===");
    foreach (var (label, loadMs, bestMs, _) in summary)
    {
        Console.WriteLine($"{label,-36} warm-up {loadMs,7:F0} ms   transcribe {bestMs,6:F0} ms   {clip.Duration.TotalMilliseconds / bestMs,5:F1}x real-time");
    }
}

return 0;

static AudioClip LoadClip(string path)
{
    using var reader = new AudioFileReader(path);
    ISampleProvider provider = reader;
    if (provider.WaveFormat.Channels > 1)
    {
        provider = provider.ToMono();
    }

    if (provider.WaveFormat.SampleRate != AudioClip.WhisperSampleRate)
    {
        provider = new WdlResamplingSampleProvider(provider, AudioClip.WhisperSampleRate);
    }

    var samples = new List<float>();
    var buffer = new float[16_000];
    int read;
    while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
    {
        samples.AddRange(buffer.Take(read));
    }

    return new AudioClip(samples.ToArray(), AudioClip.WhisperSampleRate);
}

internal sealed class StaticSettings : ISettingsProvider
{
    public StaticSettings(TawkTypeSettings settings) => Current = settings;

    public TawkTypeSettings Current { get; }

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}

internal sealed class ConsoleProgress : IProgress<ModelProgress>
{
    private int _lastPercent = -1;

    public void Report(ModelProgress value)
    {
        var percent = value.Fraction is { } f ? (int)(f * 100) : -1;
        if (percent == _lastPercent && percent >= 0)
        {
            return;
        }

        _lastPercent = percent;
        Console.WriteLine(percent >= 0 ? $"  {value.Stage}: {percent}%" : $"  {value.Stage}");
    }
}
