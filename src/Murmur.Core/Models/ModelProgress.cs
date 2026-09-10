namespace Murmur.Core.Models;

/// <summary>Progress of a model download / load. <see cref="Fraction"/> is 0..1, or null when the total is unknown.</summary>
public sealed record ModelProgress(string Stage, long BytesDone, long? BytesTotal)
{
    public double? Fraction => BytesTotal is > 0 ? Math.Clamp((double)BytesDone / BytesTotal.Value, 0, 1) : null;
}
