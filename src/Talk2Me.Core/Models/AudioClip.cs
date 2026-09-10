namespace Talk2Me.Core.Models;

/// <summary>PCM audio, mono, float samples in -1..1.</summary>
public sealed record AudioClip(float[] Samples, int SampleRate)
{
    public const int WhisperSampleRate = 16_000;

    public static AudioClip Empty { get; } = new(Array.Empty<float>(), WhisperSampleRate);

    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / SampleRate);
}
