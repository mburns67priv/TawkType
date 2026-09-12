using Microsoft.Extensions.Logging;
using TawkType.Core.Abstractions;
using TawkType.Core.Models;
using NAudio.Wave;

namespace TawkType.Windows.Audio;

/// <summary>
/// Microphone capture through WinMM (WaveIn). The driver resamples to 16 kHz mono 16-bit for us,
/// which is exactly what Whisper wants. Swap for WASAPI later if we need lower latency or loopback.
/// </summary>
public sealed class WaveInAudioCapture : IAudioCapture
{
    private const int SampleRate = AudioClip.WhisperSampleRate;
    private const int BufferMilliseconds = 50;

    private readonly ISettingsProvider _settings;
    private readonly ILogger<WaveInAudioCapture> _logger;
    private readonly object _gate = new();

    private List<float> _samples = new();
    private WaveInEvent? _waveIn;

    public WaveInAudioCapture(ISettingsProvider settings, ILogger<WaveInAudioCapture> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public event EventHandler<float>? LevelChanged;

    public bool IsCapturing { get; private set; }

    public static IReadOnlyList<string> ListInputDevices()
    {
        var names = new List<string>();
        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            names.Add(WaveInEvent.GetCapabilities(i).ProductName);
        }

        return names;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (IsCapturing)
            {
                return;
            }

            _samples = new List<float>(SampleRate * 30);
            var waveIn = new WaveInEvent
            {
                DeviceNumber = ResolveDeviceNumber(),
                WaveFormat = new WaveFormat(SampleRate, 16, 1),
                BufferMilliseconds = BufferMilliseconds,
                NumberOfBuffers = 4,
            };
            waveIn.DataAvailable += OnDataAvailable;
            waveIn.StartRecording();
            _waveIn = waveIn;
            IsCapturing = true;
        }
    }

    public AudioClip Stop()
    {
        WaveInEvent? waveIn;
        lock (_gate)
        {
            if (!IsCapturing || _waveIn is null)
            {
                return AudioClip.Empty;
            }

            waveIn = _waveIn;
            _waveIn = null;
            IsCapturing = false;
        }

        // StopRecording joins the capture thread, which may be waiting on _gate inside OnDataAvailable,
        // so it must run outside the lock.
        waveIn.DataAvailable -= OnDataAvailable;
        waveIn.StopRecording();
        waveIn.Dispose();

        lock (_gate)
        {
            var clip = new AudioClip(_samples.ToArray(), SampleRate);
            _samples = new List<float>();
            return clip;
        }
    }

    public void Dispose()
    {
        if (IsCapturing)
        {
            Stop();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var count = e.BytesRecorded / 2;
        if (count == 0)
        {
            return;
        }

        var sumOfSquares = 0f;
        lock (_gate)
        {
            if (!IsCapturing)
            {
                return;
            }

            _samples.EnsureCapacity(_samples.Count + count);
            for (var i = 0; i < count; i++)
            {
                var sample = (short)(e.Buffer[2 * i] | (e.Buffer[(2 * i) + 1] << 8));
                var value = sample / 32768f;
                _samples.Add(value);
                sumOfSquares += value * value;
            }
        }

        // Raw RMS, 0..1, the same unit the samples are in. It used to be multiplied by six and
        // clamped, which made every threshold downstream a statement about that six rather than about
        // the signal - and the numbers chosen against it were wrong for real hardware. AudioLevel.Meter
        // decides how it is drawn; MicrophoneCheck decides what it means.
        var rms = MathF.Sqrt(sumOfSquares / count);
        LevelChanged?.Invoke(this, Math.Clamp(rms, 0f, 1f));
    }

    private int ResolveDeviceNumber()
    {
        var wanted = _settings.Current.InputDeviceName;
        if (string.IsNullOrWhiteSpace(wanted))
        {
            return -1; // WAVE_MAPPER: the system default microphone
        }

        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            if (WaveInEvent.GetCapabilities(i).ProductName.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        _logger.LogWarning("Input device containing '{Name}' not found; using the default microphone", wanted);
        return -1;
    }
}
