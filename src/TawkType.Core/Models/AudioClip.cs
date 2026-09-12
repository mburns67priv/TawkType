namespace TawkType.Core.Models;

/// <summary>PCM audio, mono, float samples in -1..1.</summary>
public sealed record AudioClip(float[] Samples, int SampleRate)
{
    public const int WhisperSampleRate = 16_000;

    public static AudioClip Empty { get; } = new(Array.Empty<float>(), WhisperSampleRate);

    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / SampleRate);

    /// <summary>
    /// The loudest sample, 0..1. Logged with every dictation: it is the difference between "nothing
    /// was recognised" and "nothing was recorded", and neither the transcript nor the waveform can
    /// tell you which happened on somebody else's machine.
    /// </summary>
    public float Peak
    {
        get
        {
            var peak = 0f;

            foreach (var sample in Samples)
            {
                var magnitude = Math.Abs(sample);
                if (magnitude > peak)
                {
                    peak = magnitude;
                }
            }

            return peak;
        }
    }
}
