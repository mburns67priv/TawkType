namespace TawkType.Core.Abstractions;

/// <summary>
/// Holds the provider API key outside settings.json, which is plain text on disk. The Windows
/// implementation encrypts it with DPAPI under the current user account.
/// </summary>
public interface IApiKeyStore
{
    bool HasKey { get; }

    /// <summary>The stored key, or null when none is set.</summary>
    string? Read();

    /// <summary>Stores the key, or clears it when <paramref name="apiKey"/> is null or blank.</summary>
    void Write(string? apiKey);
}
