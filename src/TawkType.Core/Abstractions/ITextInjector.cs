namespace TawkType.Core.Abstractions;

/// <summary>Types text into whatever control currently has keyboard focus.</summary>
public interface ITextInjector
{
    Task InjectAsync(string text, CancellationToken cancellationToken = default);
}
