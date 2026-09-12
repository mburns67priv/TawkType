using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TawkType.Core.Models;
using TawkType.Core.Onboarding;
using TawkType.Core.Settings;
using TawkType.Windows.Audio;

// What the microphone on this machine actually produces, through the same capture the app uses and
// in the same units the meter and MicrophoneCheck are calibrated on.
//
// It exists because those thresholds were set from an assumption — "speech RMS sits around 0.02-0.2"
// — and the first person to run TawkType on real hardware was told his perfectly good microphone was
// too quiet. Numbers from a real voice beat numbers from a comment.
//
//   dotnet run --project tools/TawkType.Mic            listens for 8 seconds
//   dotnet run --project tools/TawkType.Mic -- 15      listens for 15

var seconds = args.Length > 0 && int.TryParse(args[0], out var requested) ? Math.Clamp(requested, 3, 120) : 8;

var settings = new SettingsStore(NullLogger<SettingsStore>.Instance);
var device = settings.Current.InputDeviceName;

Console.WriteLine();
Console.WriteLine("Devices Windows offers:");
var devices = WaveInAudioCapture.ListInputDevices();
for (var i = 0; i < devices.Count; i++)
{
    Console.WriteLine($"  [{i}] {devices[i]}");
}

Console.WriteLine();
Console.WriteLine(device is null
    ? "TawkType is set to the system default microphone."
    : $"TawkType is set to the first device containing \"{device}\".");

using var capture = new WaveInAudioCapture(settings, NullLogger<WaveInAudioCapture>.Instance);

var levels = new List<float>();
capture.LevelChanged += (_, level) => levels.Add(level);

Console.WriteLine();
Console.WriteLine($"Speak normally for {seconds} seconds — the way you would dictate. Starting now.");
Console.WriteLine();

capture.Start();

for (var elapsed = 0; elapsed < seconds; elapsed++)
{
    Thread.Sleep(1000);

    // A crude live meter, so it is obvious something is being heard while it runs.
    var recent = levels.Count == 0 ? 0 : levels.TakeLast(20).Max();
    var bars = (int)Math.Round(AudioLevel.Meter(recent) * 40);
    Console.WriteLine($"  {elapsed + 1,2}s  {new string('#', bars).PadRight(40, '.')}  {recent:F3}");
}

var clip = capture.Stop();

Console.WriteLine();
Console.WriteLine("──────────────────────────────────────────────────────────────");

if (clip.Samples.Length == 0 || levels.Count == 0)
{
    Console.WriteLine("Nothing was captured at all. Wrong device, or Windows has it muted.");
    return;
}

// LevelChanged raises raw RMS, which is the unit both the meter and MicrophoneCheck are calibrated
// on, so these numbers can be compared with the thresholds directly.
var windowPeak = levels.Max();
var windowMedian = Median(levels);

// The clip's own numbers, independent of the 50 ms windows the level events are computed over.
var samplePeak = clip.Peak;
var sampleRms = Rms(clip.Samples);

Console.WriteLine($"  captured        {clip.Duration.TotalSeconds:F1}s, {levels.Count} level readings");
Console.WriteLine();
Console.WriteLine($"  sample peak     {samplePeak:F4}  ({Decibels(samplePeak)})   loudest single sample");
Console.WriteLine($"  window RMS      {windowMedian:F4}  ({Decibels(windowMedian)})   median, mostly the gaps between words");
Console.WriteLine($"                  {windowPeak:F4}  ({Decibels(windowPeak)})   peak, which is what the verdict reads");
Console.WriteLine($"  whole clip RMS  {sampleRms:F4}  ({Decibels(sampleRms)})   silence included");
Console.WriteLine();

var reading = MicrophoneCheck.For(windowPeak);
Console.WriteLine($"  verdict         {reading.Verdict}");
Console.WriteLine($"                  {reading.Message}");
Console.WriteLine();
Console.WriteLine("  thresholds, on the peak RMS window:");
Console.WriteLine($"    silent below  {MicrophoneCheck.NoiseFloor:F4}  ({Decibels(MicrophoneCheck.NoiseFloor)})");
Console.WriteLine($"    quiet below   {MicrophoneCheck.QuietSpeech:F4}  ({Decibels(MicrophoneCheck.QuietSpeech)})");
Console.WriteLine($"    clipping over {MicrophoneCheck.Clipping:F4}  ({Decibels(MicrophoneCheck.Clipping)})");
Console.WriteLine();
Console.WriteLine($"  waveform        the median draws {3 + (AudioLevel.Meter(windowMedian) * 23):F1}px of 26, "
    + $"the peak {3 + (AudioLevel.Meter(windowPeak) * 23):F1}px");
Console.WriteLine("──────────────────────────────────────────────────────────────");
Console.WriteLine();
Console.WriteLine("Paste this whole block into the conversation if anything here looks wrong.");

static string Decibels(double amplitude)
    => amplitude <= 0 ? "  -inf dB" : $"{20 * Math.Log10(amplitude),7:F1} dB";

static float Median(List<float> values)
{
    var sorted = values.OrderBy(value => value).ToArray();
    return sorted[sorted.Length / 2];
}

static float Rms(float[] samples)
{
    double sum = 0;
    foreach (var sample in samples)
    {
        sum += (double)sample * sample;
    }

    return (float)Math.Sqrt(sum / samples.Length);
}
