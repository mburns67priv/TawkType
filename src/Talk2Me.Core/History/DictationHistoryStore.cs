using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Talk2Me.Core.Abstractions;
using Talk2Me.Core.Models;
using Talk2Me.Core.Settings;

namespace Talk2Me.Core.History;

/// <summary>
/// The dictation log, kept as JSON Lines at %LOCALAPPDATA%/Talk2Me/history.jsonl: one record per line,
/// appended as each dictation completes. A corrupt or half-written line is skipped rather than losing
/// the file. The whole log is rewritten only when it is trimmed or cleared.
/// </summary>
public sealed class DictationHistoryStore : IDictationHistory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Trimming rewrites the file, so leave headroom rather than doing it on every single add.</summary>
    private const int TrimSlack = 50;

    private readonly ISettingsProvider _settings;
    private readonly ILogger<DictationHistoryStore> _logger;
    private readonly object _gate = new();

    /// <summary>Newest first, mirroring what is on disk.</summary>
    private readonly List<DictationRecord> _records = new();

    public DictationHistoryStore(ISettingsProvider settings, ILogger<DictationHistoryStore> logger)
        : this(settings, logger, System.IO.Path.Combine(SettingsStore.AppDataDirectory, "history.jsonl"))
    {
    }

    public DictationHistoryStore(ISettingsProvider settings, ILogger<DictationHistoryStore> logger, string path)
    {
        _settings = settings;
        _logger = logger;
        Path = path;
        Load();
    }

    public string Path { get; }

    public IReadOnlyList<DictationRecord> Recent
    {
        get
        {
            lock (_gate)
            {
                // Trimming the file is deliberately lazy, so never hand out more than the user asked to keep.
                return _records.Take(MaxEntries).ToArray();
            }
        }
    }

    public DictationRecord? Last
    {
        get
        {
            lock (_gate)
            {
                return _records.Count > 0 ? _records[0] : null;
            }
        }
    }

    public event EventHandler? Changed;

    public void Add(DictationRecord record)
    {
        if (!_settings.Current.History.Enabled)
        {
            return;
        }

        lock (_gate)
        {
            _records.Insert(0, record);

            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine);

                var max = MaxEntries;
                if (_records.Count > max + TrimSlack)
                {
                    _records.RemoveRange(max, _records.Count - max);
                    Rewrite();
                }
            }
            catch (Exception ex)
            {
                // The dictation itself already worked; failing to log it must not surface as an error.
                _logger.LogWarning(ex, "Could not write the dictation history to {Path}", Path);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _records.Clear();

            try
            {
                if (File.Exists(Path))
                {
                    File.Delete(Path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete the dictation history at {Path}", Path);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private int MaxEntries => Math.Max(1, _settings.Current.History.MaxEntries);

    /// <summary>Writes the in-memory list back out, oldest line first so appends stay chronological.</summary>
    private void Rewrite()
    {
        var lines = _records
            .AsEnumerable()
            .Reverse()
            .Select(record => JsonSerializer.Serialize(record, JsonOptions));

        File.WriteAllLines(Path, lines);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return;
            }

            foreach (var line in File.ReadLines(Path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    if (JsonSerializer.Deserialize<DictationRecord>(line, JsonOptions) is { } record)
                    {
                        _records.Insert(0, record);
                    }
                }
                catch (JsonException)
                {
                    // A line torn by a crash mid-write. Skip it; the rest of the log is still good.
                }
            }

            var max = MaxEntries;
            if (_records.Count > max)
            {
                _records.RemoveRange(max, _records.Count - max);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the dictation history from {Path}; starting empty", Path);
            _records.Clear();
        }
    }
}
