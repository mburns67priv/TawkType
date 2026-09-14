using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TawkType.Windows.Audio;

/// <summary>
/// Plays a short .wav at a chosen volume, without ever getting in the way of a dictation.
///
/// NAudio rather than <c>System.Media.SoundPlayer</c> for one reason: volume. Neither
/// <c>SoundPlayer</c> nor <c>SystemSound.Play()</c> has any level control, and a volume slider was
/// the thing asked for.
///
/// Files are read into memory once and kept. A cue is a few tens of kilobytes, it is the same file
/// every time, and the alternative is a disk read at the exact moment the user has pressed the key
/// and is waiting to be told they can speak.
///
/// Every failure here is swallowed. There may be no audio device, the device may be exclusive to
/// something else, the .wav may be a format NAudio will not open. None of that is worth interrupting
/// a dictation for, and the sound is a courtesy — the bar is still saying the same thing visually.
/// </summary>
public sealed class CuePlayer : IDisposable
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>
    /// Starts the sound and returns. Never waits for it to finish: the caller is the dictation
    /// pipeline, and the start cue plays as recording begins.
    /// </summary>
    public void Play(string? path, float gain)
    {
        if (_disposed || string.IsNullOrWhiteSpace(path) || gain <= 0f)
        {
            return;
        }

        try
        {
            if (Read(path) is not { } bytes)
            {
                return;
            }

            var reader = new WaveFileReader(new MemoryStream(bytes));
            var output = new WaveOutEvent();

            // The reader and the device both have to outlive this method, and both have to be let go
            // afterwards or a sound on every dictation leaks a device handle an hour at a time.
            output.PlaybackStopped += (_, _) =>
            {
                output.Dispose();
                reader.Dispose();
            };

            output.Init(new VolumeSampleProvider(reader.ToSampleProvider()) { Volume = Math.Clamp(gain, 0f, 1f) });
            output.Play();
        }
        catch (Exception)
        {
            // No device, a busy device, or a .wav this version cannot open.
        }
    }

    public void Dispose() => _disposed = true;

    /// <summary>The file's bytes, read once. A file that cannot be read is remembered as unreadable.</summary>
    private byte[]? Read(string path)
    {
        lock (_gate)
        {
            if (_files.TryGetValue(path, out var cached))
            {
                return cached.Length == 0 ? null : cached;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception)
            {
                // Remembered as empty so a missing file is not retried on every single dictation.
                _files[path] = [];
                return null;
            }

            _files[path] = bytes;
            return bytes;
        }
    }
}
