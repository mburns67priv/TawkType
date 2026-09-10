using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Settings;

namespace Talk2Me.Windows.Security;

/// <summary>
/// Keeps the API key in %LOCALAPPDATA%\Talk2Me\apikey.dat, encrypted with DPAPI under the current user
/// account. Another account on the machine cannot read it; anything running as this user can, so this
/// protects the file at rest, not against local malware.
/// </summary>
public sealed class DpapiApiKeyStore : IApiKeyStore
{
    // Bound into the ciphertext: a file copied from another install will not decrypt as something else.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Talk2Me.ApiKey.v1");

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
        try
        {
            if (!File.Exists(Path))
            {
                return null;
            }

            var plain = ProtectedData.Unprotect(File.ReadAllBytes(Path), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex)
        {
            // Wrong user, roamed profile, or a corrupt file. Treat it as "no key" and let the user re-enter it.
            _logger.LogWarning(ex, "Could not read the stored API key from {Path}", Path);
            return null;
        }
    }
}
