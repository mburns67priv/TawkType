using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using TawkType.Core.Abstractions;
using TawkType.Core.Settings;

namespace TawkType.Windows.Security;

/// <summary>
/// Keeps the API key in the app data folder's apikey.dat, encrypted with DPAPI under the current user
/// account. Another account on the machine cannot read it; anything running as this user can, so this
/// protects the file at rest, not against local malware.
/// </summary>
public sealed class DpapiApiKeyStore : IApiKeyStore
{
    /// <summary>
    /// Additional entropy, bound into the ciphertext so a file copied from another install will not
    /// decrypt as something else. Changing it makes every existing apikey.dat undecryptable and there
    /// is no fallback that would rescue one, so do not change it without writing one first.
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TawkType.ApiKey.v1");

    private readonly ILogger<DpapiApiKeyStore> _logger;
    private readonly object _gate = new();

    private string? _cached;
    private bool _loaded;

    public DpapiApiKeyStore(ILogger<DpapiApiKeyStore> logger)
        : this(logger, System.IO.Path.Combine(SettingsStore.AppDataDirectory, "apikey.dat"))
    {
    }

    public DpapiApiKeyStore(ILogger<DpapiApiKeyStore> logger, string path)
    {
        _logger = logger;
        Path = path;
    }

    public string Path { get; }

    public bool HasKey => !string.IsNullOrEmpty(Read());

    public string? Read()
    {
        lock (_gate)
        {
            if (_loaded)
            {
                return _cached;
            }

            _loaded = true;
            _cached = Decrypt();
            return _cached;
        }
    }

    public void Write(string? apiKey)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                if (File.Exists(Path))
                {
                    File.Delete(Path);
                }

                _cached = null;
                _loaded = true;
                return;
            }

            var trimmed = apiKey.Trim();
            var cipher = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(trimmed),
                Entropy,
                DataProtectionScope.CurrentUser);

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllBytes(Path, cipher);

            _cached = trimmed;
            _loaded = true;
        }
    }

    private string? Decrypt()
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        byte[] cipher;

        try
        {
            cipher = File.ReadAllBytes(Path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the stored API key from {Path}", Path);
            return null;
        }

        if (TryUnprotect(cipher, Entropy) is { } key)
        {
            return key;
        }

        // Wrong user, a roamed profile, or a corrupt file. "No key" is recoverable — the user is
        // asked for it again — in a way that pretending otherwise would not be.
        _logger.LogWarning("Could not decrypt the stored API key at {Path}", Path);
        return null;
    }

    private static string? TryUnprotect(byte[] cipher, byte[] entropy)
    {
        try
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(cipher, entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
